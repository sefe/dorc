import { firstValueFrom } from 'rxjs';
import { PropertyValuesApi } from '../apis/dorc-api';
import type {
  PropertyResolutionPreviewRequestApiModel,
  PropertyResolutionTraceApiModel,
  RequestProperty
} from '../apis/dorc-api';
import { dorcApiConfiguration } from '../services/dorc-api-configuration';

export interface PropertyResolutionContext {
  environmentName?: string;
  projectName?: string;
  requestOverrides?: RequestProperty[];
  locationKind?:
    | 'deploy-request'
    | 'script-arguments'
    | 'bundle-request'
    | 'deployment-definition'
    | 'ephemeral-wizard'
    | 'raw-json';
}

export type PropertyResolutionTransport = (
  request: PropertyResolutionPreviewRequestApiModel
) => Promise<PropertyResolutionTraceApiModel[]>;

interface CachedTraceEntry {
  trace: PropertyResolutionTraceApiModel;
  expiresAt: number;
}

interface PendingBatchBucket {
  context: PropertyResolutionContext;
  propertyNames: Map<string, string>;
  resolvers: Array<{
    requestedNames: string[];
    resolve: (result: Map<string, PropertyResolutionTraceApiModel>) => void;
    reject: (reason: unknown) => void;
  }>;
  timerId: ReturnType<typeof setTimeout>;
}

const DEFAULT_TTL_MS = 30_000;
const DEFAULT_COALESCE_WINDOW_MS = 50;

function defaultTransport(
  request: PropertyResolutionPreviewRequestApiModel
): Promise<PropertyResolutionTraceApiModel[]> {
  const api = new PropertyValuesApi(dorcApiConfiguration);
  return firstValueFrom(
    api.apiPropertyValuesResolvePreviewPost({
      propertyResolutionPreviewRequestApiModel: request
    })
  );
}

export function computeResolutionContextKey(
  context: PropertyResolutionContext
): string {
  const env = (context.environmentName ?? '').trim().toLowerCase();
  const proj = (context.projectName ?? '').trim().toLowerCase();
  const overrides = (context.requestOverrides ?? [])
    .filter(o => Boolean(o.PropertyName?.trim()))
    .map(o => ({
      name: o.PropertyName!.trim().toLowerCase(),
      val: o.IsSensitive ? '[REDACTED]' : (o.PropertyValue ?? ''),
      sec: Boolean(o.IsSensitive)
    }))
    .sort((a, b) => a.name.localeCompare(b.name));

  return JSON.stringify({ env, proj, overrides });
}

export class PropertyResolutionClientCache {
  private readonly cache = new Map<string, Map<string, CachedTraceEntry>>();
  private readonly pendingBuckets = new Map<string, PendingBatchBucket>();
  private readonly transport: PropertyResolutionTransport;
  private readonly ttlMs: number;
  private readonly coalesceWindowMs: number;

  constructor(options?: {
    transport?: PropertyResolutionTransport;
    ttlMs?: number;
    coalesceWindowMs?: number;
  }) {
    this.transport = options?.transport ?? defaultTransport;
    this.ttlMs = options?.ttlMs ?? DEFAULT_TTL_MS;
    this.coalesceWindowMs =
      options?.coalesceWindowMs ?? DEFAULT_COALESCE_WINDOW_MS;
  }

  clear(): void {
    this.cache.clear();
  }

  resolveProperties(
    context: PropertyResolutionContext,
    propertyNames: string[]
  ): Promise<Map<string, PropertyResolutionTraceApiModel>> {
    const cleanNames = Array.from(
      new Set(propertyNames.map(n => n.trim()).filter(Boolean))
    );

    if (cleanNames.length === 0) {
      return Promise.resolve(new Map());
    }

    const contextKey = computeResolutionContextKey(context);
    const now = Date.now();
    const contextCache = this.cache.get(contextKey);
    const missingNames: string[] = [];

    if (contextCache) {
      for (const name of cleanNames) {
        const entry = contextCache.get(name.toLowerCase());
        if (!entry || entry.expiresAt <= now) {
          missingNames.push(name);
        }
      }
    } else {
      missingNames.push(...cleanNames);
    }

    if (missingNames.length === 0) {
      return Promise.resolve(this.collectCached(contextKey, cleanNames));
    }

    return new Promise((resolve, reject) => {
      let bucket = this.pendingBuckets.get(contextKey);
      if (!bucket) {
        const timerId = setTimeout(
          () => this.flushBucket(contextKey),
          this.coalesceWindowMs
        );
        bucket = {
          context,
          propertyNames: new Map(),
          resolvers: [],
          timerId
        };
        this.pendingBuckets.set(contextKey, bucket);
      }

      for (const missing of missingNames) {
        const lower = missing.toLowerCase();
        if (!bucket.propertyNames.has(lower)) {
          bucket.propertyNames.set(lower, missing);
        }
      }

      bucket.resolvers.push({
        requestedNames: cleanNames,
        resolve,
        reject
      });
    });
  }

  private collectCached(
    contextKey: string,
    requestedNames: string[]
  ): Map<string, PropertyResolutionTraceApiModel> {
    const out = new Map<string, PropertyResolutionTraceApiModel>();
    const contextCache = this.cache.get(contextKey)!;
    for (const name of requestedNames) {
      const entry = contextCache.get(name.toLowerCase());
      if (entry) {
        out.set(name.toLowerCase(), entry.trace);
      }
    }
    return out;
  }

  private async flushBucket(contextKey: string): Promise<void> {
    const bucket = this.pendingBuckets.get(contextKey)!;
    this.pendingBuckets.delete(contextKey);

    const namesToFetch = Array.from(bucket.propertyNames.values());
    try {
      const traces = await this.transport({
        EnvironmentName: bucket.context.environmentName ?? null,
        ProjectName: bucket.context.projectName ?? null,
        PropertyNames: namesToFetch,
        RequestOverrides: bucket.context.requestOverrides ?? null
      });

      const expiresAt = Date.now() + this.ttlMs;
      let contextCache = this.cache.get(contextKey);
      if (!contextCache) {
        contextCache = new Map();
        this.cache.set(contextKey, contextCache);
      }

      for (const trace of traces) {
        if (trace.PropertyName) {
          contextCache.set(trace.PropertyName.toLowerCase(), {
            trace,
            expiresAt
          });
        }
      }

      for (const waiter of bucket.resolvers) {
        waiter.resolve(this.collectCached(contextKey, waiter.requestedNames));
      }
    } catch (err) {
      for (const waiter of bucket.resolvers) {
        waiter.reject(err);
      }
    }
  }
}

export const defaultPropertyResolutionClientCache =
  new PropertyResolutionClientCache();
