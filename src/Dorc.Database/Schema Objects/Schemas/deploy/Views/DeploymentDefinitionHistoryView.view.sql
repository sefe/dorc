CREATE VIEW [deploy].[DeploymentDefinitionHistoryView] AS
SELECT CAST('change:' + CONVERT(VARCHAR(36), c.Id) + ':' + COALESCE(CONVERT(VARCHAR(12), COALESCE(affected.ComponentId, r.ComponentId)), 'project') AS NVARCHAR(128)) AS Id,
    CAST(c.Operation AS NVARCHAR(64)) AS EventType, c.OccurredAt, c.Actor, c.ProjectId,
    COALESCE(affected.ComponentId, r.ComponentId) AS ComponentId, CAST(COALESCE(affected.ComponentName, r.ComponentName, '') AS NVARCHAR(256)) AS ComponentName,
    CAST('Committed definition change' AS NVARCHAR(1024)) AS Summary,
    CAST(NULL AS INT) AS RequestId, CAST(NULL AS INT) AS DeploymentResultId,
    CAST(r.Revision AS NVARCHAR(64)) AS DefinitionRevision, c.BeforeJson, c.AfterJson,
    CAST(0 AS BIT) AS IsLegacy, CAST(NULL AS NVARCHAR(1024)) AS SourceSummary
FROM [deploy].[DeploymentDefinitionChange] c
OUTER APPLY OPENJSON(COALESCE(c.AffectedComponentsJson, N'[]'))
    WITH (ComponentId INT, ComponentName NVARCHAR(256)) affected
LEFT JOIN [deploy].[DeploymentDefinitionRevision] r ON r.ChangeId = c.Id AND (affected.ComponentId IS NULL OR r.ComponentId = affected.ComponentId)
UNION ALL
SELECT 'submitted:' + CONVERT(VARCHAR(12), d.DeploymentRequestId) + ':' + CONVERT(VARCHAR(12), d.Ordinal),
    'Request.Submitted', d.SubmittedAt, d.SubmittedBy, d.ProjectId, d.ComponentId, d.ComponentName,
    CASE WHEN d.IsSkipped = 1 THEN 'Submitted definition (skipped)' ELSE 'Submitted definition (external inputs remain dynamic)' END,
    d.DeploymentRequestId, NULL, d.DefinitionRevision, NULL, NULL, CAST(0 AS BIT),
    'Submitted revision ' + d.DefinitionRevision
FROM [deploy].[DeploymentRequestDefinition] d
UNION ALL
SELECT 'powershell:' + CONVERT(VARCHAR(12), p.DeploymentResultId) + ':' + CONVERT(VARCHAR(12), p.ScriptOrdinal),
    'PowerShell.' + p.Phase, COALESCE(p.CompletedAt, p.InvocationObservedAt, p.PreparedAt, p.ObservedAt), '',
    p.ProjectId, p.ComponentId, d.ComponentName, 'Observed PowerShell execution evidence',
    p.DeploymentRequestId, p.DeploymentResultId, p.DefinitionRevision, NULL, NULL, CAST(0 AS BIT),
    CASE WHEN p.TextSha256 = '' THEN 'Executed source not recorded' ELSE 'PowerShell text SHA256 ' + p.TextSha256 END
FROM [deploy].[PowerShellExecutionReceipt] p
JOIN [deploy].[DeploymentRequestDefinition] d ON d.DeploymentRequestId = p.DeploymentRequestId AND d.ComponentId = p.ComponentId
UNION ALL
SELECT 'terraform:' + CONVERT(VARCHAR(12), t.DeploymentResultId), 'Terraform.Planned', t.PlannedAt, '',
    d.ProjectId, t.ComponentId, t.ComponentName, 'Plan-time source and saved-plan receipt',
    t.DeploymentRequestId, t.DeploymentResultId, d.DefinitionRevision, NULL, NULL, CAST(0 AS BIT),
    COALESCE('Git ' + t.GitCommitSha, 'Source SHA256 ' + t.SourceSha256)
FROM [deploy].[TerraformDeploymentReceipt] t
JOIN [deploy].[DeploymentRequestDefinition] d ON d.DeploymentRequestId = t.DeploymentRequestId AND d.ComponentId = t.ComponentId
UNION ALL
SELECT 'approval:' + CONVERT(VARCHAR(12), a.Id), 'Terraform.Approved', a.ApprovedAt, a.ApprovedBy,
    d.ProjectId, d.ComponentId, d.ComponentName, 'Saved plan approved',
    a.DeploymentRequestId, a.DeploymentResultId, d.DefinitionRevision, NULL, NULL, CAST(0 AS BIT), NULL
FROM [deploy].[TerraformPlanApproval] a
JOIN [deploy].[TerraformDeploymentReceipt] t ON t.DeploymentResultId = a.DeploymentResultId
JOIN [deploy].[DeploymentRequestDefinition] d ON d.DeploymentRequestId = a.DeploymentRequestId AND d.ComponentId = t.ComponentId
UNION ALL
SELECT 'outcome:' + CONVERT(VARCHAR(12), o.DeploymentResultId), 'Terraform.Outcome', o.AppliedAt, '',
    d.ProjectId, d.ComponentId, d.ComponentName,
    'Apply: ' + o.ApplyOutcome + '; inventory: ' + o.InventoryOutcome + '; archive: ' + o.ArchiveOutcome,
    d.DeploymentRequestId, o.DeploymentResultId, d.DefinitionRevision, NULL, NULL, CAST(0 AS BIT), NULL
FROM [deploy].[TerraformDeploymentOutcome] o
JOIN [deploy].[TerraformDeploymentReceipt] t ON t.DeploymentResultId = o.DeploymentResultId
JOIN [deploy].[DeploymentRequestDefinition] d ON d.DeploymentRequestId = t.DeploymentRequestId AND d.ComponentId = t.ComponentId
WHERE o.AppliedAt IS NOT NULL
UNION ALL
SELECT 'legacy-refdata:' + CONVERT(VARCHAR(12), a.RefDataAuditId), 'Legacy.RefDataSubmission',
    TODATETIMEOFFSET(a.[Date], '+00:00'), COALESCE(a.Username, ''), a.ProjectId, NULL, '',
    'Legacy submitted JSON; successful mutation and original timezone are not verified',
    NULL, NULL, NULL, NULL, NULL, CAST(1 AS BIT), NULL
FROM [deploy].[RefDataAudit] a
WHERE a.ProjectId IS NOT NULL AND a.ChangeId IS NULL
UNION ALL
SELECT 'legacy-script:' + CONVERT(VARCHAR(20), a.Id) + ':' + CONVERT(VARCHAR(12), p.ProjectId) + ':' + CONVERT(VARCHAR(12), c.Id),
    'Legacy.ScriptAudit', TODATETIMEOFFSET(a.UpdatedDate, '+00:00'), COALESCE(a.UpdatedBy, ''),
    p.ProjectId, c.Id, COALESCE(a.ScriptName, ''), 'Legacy script audit; execution and original timezone are not verified',
    NULL, NULL, NULL, NULL, NULL, CAST(1 AS BIT), NULL
FROM [deploy].[AuditScript] a
JOIN [deploy].[Component] c ON c.ScriptId = a.ScriptId
JOIN [deploy].[ProjectComponent] p ON p.ComponentId = c.Id
WHERE COALESCE(a.FromValue, '') <> 'See deployment definition changes'
UNION ALL
SELECT 'baseline:' + CONVERT(VARCHAR(36), r.Id), 'Baseline.Observed', r.ObservedAt, '',
    r.ProjectId, r.ComponentId, r.ComponentName, 'Current definition first observed; not an original creation event',
    NULL, NULL, r.Revision, NULL, r.DefinitionJson, CAST(0 AS BIT), NULL
FROM [deploy].[DeploymentDefinitionRevision] r WHERE r.IsBaseline = 1
UNION ALL
SELECT 'cancelled:' + CONVERT(VARCHAR(12), r.Id) + ':' + CONVERT(VARCHAR(12), d.ComponentId),
    'Request.Cancelled', r.CancelledTime, COALESCE(r.CancelledBy, ''), d.ProjectId, d.ComponentId,
    d.ComponentName, 'Request cancelled; this also includes declined plans',
    r.Id, NULL, d.DefinitionRevision, NULL, NULL, CAST(0 AS BIT), NULL
FROM [deploy].[DeploymentRequest] r
JOIN [deploy].[DeploymentRequestDefinition] d ON d.DeploymentRequestId = r.Id
WHERE r.CancelledTime IS NOT NULL
UNION ALL
SELECT 'inventory:' + CONVERT(VARCHAR(12), i.ComponentId) + ':' + CONVERT(VARCHAR(12), i.EnvironmentId) + ':' + CONVERT(VARCHAR(12), i.CloudResourceId),
    'Terraform.InventoryObserved', i.LastSeenAt, '', d.ProjectId, d.ComponentId, d.ComponentName,
    'Latest inventory observation: ' + i.ReconciliationState, d.DeploymentRequestId, i.LastSeenResultId,
    d.DefinitionRevision, NULL, NULL, CAST(0 AS BIT), NULL
FROM [deploy].[TerraformInventoryProvenance] i
JOIN [deploy].[TerraformDeploymentReceipt] t ON t.DeploymentResultId = i.LastSeenResultId
JOIN [deploy].[DeploymentRequestDefinition] d ON d.DeploymentRequestId = t.DeploymentRequestId AND d.ComponentId = i.ComponentId;
