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
  generation: number;
  propertyNames: Map<string, string>;
  resolvers: Array<{
    requestedNames: string[];
    preCached: Map<string, PropertyResolutionTraceApiModel>;
    resolve: (result: Map<string, PropertyResolutionTraceApiModel>) => void;
    reject: (reason: unknown) => void;
  }>;
  timerId: ReturnType<typeof setTimeout>;
}

const DEFAULT_TTL_MS = 30_000;
const DEFAULT_COALESCE_WINDOW_MS = 50;
const MAX_BATCH_PROPERTY_NAMES = 250;
const MAX_CACHED_CONTEXTS = 32;

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
      name: o.PropertyName!.trim(),
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
  private cacheGeneration = 0;

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
    this.cacheGeneration++;
    this.cache.clear();
  }

  private evictExpiredEntries(now: number): void {
    for (const [ctxKey, ctxMap] of this.cache.entries()) {
      for (const [propName, entry] of ctxMap.entries()) {
        if (entry.expiresAt <= now) {
          ctxMap.delete(propName);
        }
      }
      if (ctxMap.size === 0) {
        this.cache.delete(ctxKey);
      }
    }
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
    this.evictExpiredEntries(now);
    const contextCache = this.cache.get(contextKey);
    const missingNames: string[] = [];
    const preCached = new Map<string, PropertyResolutionTraceApiModel>();

    if (contextCache) {
      for (const name of cleanNames) {
        const hit = contextCache.get(name);
        if (hit) {
          preCached.set(name, hit.trace);
        } else {
          missingNames.push(name);
        }
      }
    } else {
      missingNames.push(...cleanNames);
    }

    if (missingNames.length === 0) {
      return Promise.resolve(preCached);
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
          generation: this.cacheGeneration,
          propertyNames: new Map(),
          resolvers: [],
          timerId
        };
        this.pendingBuckets.set(contextKey, bucket);
      }

      for (const missing of missingNames) {
        if (!bucket.propertyNames.has(missing)) {
          bucket.propertyNames.set(missing, missing);
        }
      }

      bucket.resolvers.push({
        requestedNames: cleanNames,
        preCached,
        resolve,
        reject
      });
    });
  }

  private async flushBucket(contextKey: string): Promise<void> {
    const bucket = this.pendingBuckets.get(contextKey)!;
    this.pendingBuckets.delete(contextKey);

    const namesToFetch = Array.from(bucket.propertyNames.values());
    const chunks: string[][] = [];
    for (let i = 0; i < namesToFetch.length; i += MAX_BATCH_PROPERTY_NAMES) {
      chunks.push(namesToFetch.slice(i, i + MAX_BATCH_PROPERTY_NAMES));
    }

    try {
      const chunkResults = await Promise.all(
        chunks.map(chunk =>
          this.transport({
            EnvironmentName: bucket.context.environmentName ?? null,
            ProjectName: bucket.context.projectName ?? null,
            PropertyNames: chunk,
            RequestOverrides: bucket.context.requestOverrides ?? null
          })
        )
      );

      const expiresAt = Date.now() + this.ttlMs;
      let contextCache = this.cache.get(contextKey);
      const transientTraces = new Map<
        string,
        PropertyResolutionTraceApiModel
      >();

      for (const traces of chunkResults) {
        for (const trace of traces) {
          if (trace.PropertyName) {
            const propKey = trace.PropertyName;
            transientTraces.set(propKey, trace);
            if (bucket.generation === this.cacheGeneration) {
              if (trace.IsSecure) {
                contextCache?.delete(propKey);
              } else {
                if (!contextCache) {
                  if (this.cache.size >= MAX_CACHED_CONTEXTS) {
                    const oldestKey = this.cache.keys().next().value!;
                    this.cache.delete(oldestKey);
                  }
                  contextCache = new Map();
                  this.cache.set(contextKey, contextCache);
                }
                contextCache.set(propKey, {
                  trace,
                  expiresAt
                });
              }
            }
          }
        }
      }

      for (const waiter of bucket.resolvers) {
        const out = new Map<string, PropertyResolutionTraceApiModel>();
        for (const name of waiter.requestedNames) {
          const result =
            waiter.preCached.get(name) ??
            transientTraces.get(name) ??
            contextCache?.get(name)?.trace;
          if (result) {
            out.set(name, result);
          }
        }
        waiter.resolve(out);
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
