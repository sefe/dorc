# Encryption Migration Tool

This tool migrates existing property values from legacy AES encryption to post-quantum resistant AES-256-GCM encryption.

## Overview

DOrc has been upgraded to use quantum-resistant encryption for secure property values:

- **Legacy Format**: AES-CBC encryption (unversioned or `v1:` prefixed)
- **New Format**: AES-256-GCM encryption (`v2:` prefixed) with authenticated encryption

The new encryption provides:
- Quantum-resistant symmetric encryption (AES-256-GCM)
- Authenticated encryption preventing tampering
- Backward compatibility with legacy encrypted values
- Version tracking for smooth migration

## Usage

### Prerequisites

1. Valid `appsettings.json` with database connection string:
```json
{
  "ConnectionStrings": {
    "DOrcConnectionString": "Server=...;Database=...;..."
  }
}
```

2. Database access with read/write permissions to PropertyValues table

### Command Line Options

```bash
# Dry run (no changes made, just reports what would be migrated)
dotnet Tools.EncryptionMigrationCLI.dll --dry-run

# Actual migration
dotnet Tools.EncryptionMigrationCLI.dll

# Force migration including v1: prefixed values
dotnet Tools.EncryptionMigrationCLI.dll --force
```

### Migration Process

1. **Dry Run First** (recommended):
   ```bash
   dotnet Tools.EncryptionMigrationCLI.dll --dry-run
   ```
   This shows how many values will be migrated without making changes.

2. **Backup Database**:
   Before running actual migration, backup your database.

3. **Run Migration**:
   ```bash
   dotnet Tools.EncryptionMigrationCLI.dll
   ```

4. **Verify**:
   Check the logs for any failed migrations. The tool will report:
   - Total property values processed
   - Successfully migrated
   - Skipped (already migrated)
   - Failed (errors)

### Migration Behavior

- **Unversioned values**: Automatically migrated to `v2:`
- **`v1:` prefixed values**: Migrated only with `--force` flag
- **`v2:` prefixed values**: Skipped (already migrated)
- **Null values**: Skipped

### Backward Compatibility

The upgraded system can decrypt:
- Legacy unversioned AES-encrypted values
- `v1:` prefixed AES-encrypted values  
- `v2:` prefixed AES-GCM-encrypted values

This allows gradual migration without service disruption.

### Exit Codes

- `0`: Success (no failures)
- `1`: Failure (one or more values failed to migrate, or configuration error)

## Technical Details

### Encryption Specifications

**Legacy (v1)**:
- Algorithm: AES-CBC
- Key size: Variable (128-256 bits)
- IV: 16 bytes
- No authentication

**Post-Quantum (v2)**:
- Algorithm: AES-256-GCM
- Key size: 256 bits (derived via SHA-256 if needed)
- Nonce: 12 bytes (random per encryption)
- Tag: 16 bytes (authentication)
- Provides authenticated encryption (confidentiality + integrity)

### Migration Safety

- Idempotent: Running multiple times won't re-encrypt v2 values
- Atomic: Each property value is migrated in a separate transaction
- Fail-safe: Failures are logged but don't stop other migrations
- Reversible: Original legacy decryption still works if needed

## Troubleshooting

### Connection Errors
Ensure `DOrcConnectionString` in `appsettings.json` is correct and accessible.

### Migration Failures
Check log files for specific errors. Common issues:
- Database permissions
- Corrupted encrypted data
- Key mismatch

### Performance
For large databases, migration may take time. Use `--dry-run` first to estimate.

## Security Notes

- Keep `appsettings.json` secure (contains connection string)
- Backup database before migration
- Monitor logs for any decryption failures
- After successful migration, all new encryptions use v2 format automatically
