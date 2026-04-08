# RabbitMQ Distributed Lock Integration Tests

This directory contains integration tests for the RabbitMQ distributed lock service that verify the High Availability implementation.

## Purpose

These tests validate:

1. **Race Condition Fix**: Only one monitor can acquire a lock at a time
2. **Queue Cleanup**: Queues are properly deleted after deployment completion
3. **Lock Release**: Locks can be re-acquired after being released
4. **Concurrent Access**: Multiple monitors competing for the same lock
5. **Timeout Handling**: Lock acquisition doesn't hang indefinitely

## Requirements

To run these integration tests, you need:

### 1. RabbitMQ Server

A running RabbitMQ instance with:
- OAuth 2.0 authentication plugin enabled
- Management plugin (optional, for verification)
- Accessible network connection

### 2. OAuth 2.0 Provider

An OAuth 2.0 server that can issue tokens for RabbitMQ access with:
- Client credentials flow support
- Token endpoint URL
- Client ID and secret configured

### 3. Environment Variables

Set these environment variables before running tests:

```bash
export RABBITMQ_HOST="your-rabbitmq-host"
export RABBITMQ_PORT="5672"
export RABBITMQ_OAUTH_CLIENT_ID="your-client-id"
export RABBITMQ_OAUTH_CLIENT_SECRET="your-client-secret"
export RABBITMQ_OAUTH_TOKEN_ENDPOINT="https://your-oauth-server/oauth/token"
```

## Running the Tests

### Enable Integration Tests

The tests are marked with `[Ignore]` by default. To run them:

1. Remove or comment out the `[Ignore]` attribute in `RabbitMqLockIntegrationTests.cs`
2. Ensure RabbitMQ and OAuth are configured
3. Run the tests:

```bash
# Run all integration tests
dotnet test --filter "Category=Integration&Category=RabbitMQ" --configuration Release

# Run specific test
dotnet test --filter "FullyQualifiedName~TwoMonitors_OnlyOneCanAcquireLock" --configuration Release

# Run with detailed logging
dotnet test --filter "Category=Integration&Category=RabbitMQ" --logger "console;verbosity=detailed"
```

## Test Scenarios

### 1. TwoMonitors_OnlyOneCanAcquireLock

**Purpose**: Verify the race condition fix

**Scenario**:
- Two monitor instances try to acquire lock for the same environment
- Only one should succeed
- Second should return null (lock already held)

**Expected Result**: 
- Monitor 1: Lock acquired
- Monitor 2: Returns null

### 2. LockRelease_AllowsSubsequentAcquisition

**Purpose**: Verify lock release properly frees resources

**Scenario**:
- Monitor 1 acquires and releases lock
- Monitor 2 then tries to acquire the same lock

**Expected Result**:
- Monitor 2 successfully acquires the lock after Monitor 1 releases it

### 3. LockDisposal_DeletesQueue

**Purpose**: Verify queue cleanup prevents accumulation

**Scenario**:
- Acquire lock (creates queue)
- Release lock (should delete queue)
- Try to acquire lock again (should create fresh queue)

**Expected Result**:
- Queue is deleted after disposal
- Can re-acquire with fresh queue
- No orphaned queues in RabbitMQ

### 4. ConcurrentLockAttempts_OnlyOneSucceeds

**Purpose**: Test real-world concurrent scenario

**Scenario**:
- 5 monitors simultaneously try to acquire same lock

**Expected Result**:
- Exactly 1 monitor acquires the lock
- Exactly 4 monitors return null

### 5. LockAcquisition_WithTimeout_DoesNotHangIndefinitely

**Purpose**: Verify timeout handling

**Scenario**:
- Attempt lock acquisition with cancellation token
- Should complete within reasonable time

**Expected Result**:
- Completes within 15 seconds (doesn't hang)

### 6. QueueWithExistingMessage_PreventsDuplicateLockAcquisition

**Purpose**: Directly test the fix for split deployment issue

**Scenario**:
- Monitor 1 acquires lock (publishes message to queue)
- Monitor 2 tries to acquire same lock
- Monitor 2 should check queue message count via QueueDeclarePassiveAsync

**Expected Result**:
- Monitor 2 finds MessageCount > 0
- Monitor 2 returns null without publishing new message
- Prevents the split deployment bug

## Troubleshooting

### Tests Fail to Connect

**Issue**: Tests return null immediately

**Solutions**:
- Verify RabbitMQ is running: `telnet <host> 5672`
- Check firewall rules allow connection
- Verify OAuth credentials are correct
- Check RabbitMQ logs for authentication errors

### OAuth Token Errors

**Issue**: "ACCESS_REFUSED" errors in logs

**Solutions**:
- Verify client ID and secret are correct
- Check token endpoint URL is accessible
- Ensure OAuth token has correct permissions for RabbitMQ
- Verify token hasn't expired

### Queue Deletion Fails

**Issue**: Queues remain after test completion

**Solutions**:
- Ensure monitor has permission to delete queues
- Check RabbitMQ logs for permission errors
- Manually clean up queues via management UI

## Verifying Results

### Using RabbitMQ Management UI

1. Open RabbitMQ Management: `http://your-rabbitmq-host:15672`
2. Navigate to **Queues** tab
3. Look for queues named `lock.env:*`
4. After tests complete, these queues should be deleted

### Using RabbitMQ CLI

```bash
# List all queues
rabbitmqctl list_queues name messages consumers

# Check specific queue
rabbitmqctl list_queues name | grep "lock.env:"
```

## Expected Behavior

### Before Tests
- No `lock.env:*` queues exist
- Clean RabbitMQ state

### During Tests
- Temporary `lock.env:TestEnv-<guid>` queues created
- Each test uses unique GUID to avoid conflicts

### After Tests
- All test queues deleted
- No orphaned queues remain
- Clean state restored

## Related Issues

These tests verify the fixes for:

- **Issue #421**: High Availability for Monitor
- **Split Deployment Bug**: Requests split across "dorc ut 01" and "live dorc"
- **Queue Accumulation**: Queues not cleaned up after deployment

## Notes

- Tests use unique GUIDs for queue names to avoid conflicts between test runs
- Tests are isolated - one test failure shouldn't affect others
- Integration tests require more time than unit tests (network I/O, RabbitMQ operations)
- Consider running integration tests in CI/CD with containerized RabbitMQ
