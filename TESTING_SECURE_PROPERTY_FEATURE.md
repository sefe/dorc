# Testing Secure Property Feature

This document explains how to test the automatic encryption feature when a property is changed from non-secure to secure.

## Feature Overview

When a property's `Secure` flag is changed from `false` to `true` via the Properties API or UI, all existing property values for that property are automatically encrypted in the database.

## Testing Methods

### 1. UI Testing (Recommended)

#### Prerequisites
- User must have Power User or Admin privileges
- Access to the Variables page in the web interface

#### Steps
1. Navigate to the Variables page (Properties management)
2. Select an existing property that is currently **non-secure** (Secure checkbox is unchecked)
3. Check the "Secure" checkbox for that property
4. The system will:
   - Automatically call the Properties PUT API
   - Encrypt all existing property values for that property
   - Show success notifications confirming the action

#### Expected Results
- Success notification: "Property '[PropertyName]' secured successfully"
- Additional notification: "Existing property values for '[PropertyName]' have been automatically encrypted"
- The checkbox remains checked
- All existing property values are now encrypted in the database

#### Error Handling
- If the update fails, the checkbox automatically reverts to its previous state
- Error messages are displayed to the user

### 2. API Testing

#### Prerequisites
- API access with appropriate authentication
- Existing property with non-secure values

#### Steps
1. Create a test property with `Secure: false`:
   ```http
   POST /api/Properties
   Content-Type: application/json
   {
     "TestProperty": {
       "Name": "TestProperty",
       "Secure": false
     }
   }
   ```

2. Add some property values:
   ```http
   POST /api/PropertyValues
   Content-Type: application/json
   {
     "PropertyName": "TestProperty",
     "Value": "plaintext-value-1",
     "PropertyValueFilter": "environment1"
   }
   ```

3. Change the property to secure:
   ```http
   PUT /api/Properties
   Content-Type: application/json
   {
     "TestProperty": {
       "Name": "TestProperty",
       "Secure": true
     }
   }
   ```

#### Expected Results
- API returns success response
- All existing property values are automatically encrypted
- Future property values will be stored encrypted

### 3. Database Verification

After performing the above tests, you can verify the encryption worked by:

1. Checking the database directly to see that property values are encrypted
2. Retrieving the property via API to confirm it's marked as secure
3. Viewing property values through the UI (they should be displayed as masked)

## UI Changes Made

The following changes were made to enable UI testing:

1. **Checkbox Enable**: The "Secure" checkbox for existing properties is now enabled for Power Users and Admins (previously disabled)
2. **Event Handler**: Added `updatePropertySecure` method to handle checkbox changes
3. **API Integration**: Checkbox changes trigger the Properties PUT API
4. **User Feedback**: Success/error notifications inform users of the action result
5. **Error Recovery**: Failed updates automatically revert the checkbox state

## Code Files Modified

- `src/dorc-web/src/pages/page-variables.ts`: Added UI functionality
- `src/Dorc.Api/Services/PropertiesService.cs`: Backend encryption logic
- `src/Dorc.Api.Tests/PropertiesServiceTests.cs`: Comprehensive test coverage

## Security Considerations

- Only users with Power User or Admin privileges can change property security settings
- Encryption is performed using the existing `PropertyEncryptor` service
- The feature is backward compatible and doesn't affect existing functionality
- All operations are logged for audit purposes