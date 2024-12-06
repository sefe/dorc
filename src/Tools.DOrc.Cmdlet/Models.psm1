Class Property
{
    [string]$Name;
    [bool]$Secure;
}
class CsvProperties
{
    [string]$PropertyName;
    [bool]$IsSecured;
    [string]$Value;
    [string]$Environment;
}
class ApiConnection
{
    [string]$Property;
    [string]$PropertyValues;
    [string]$Root;
}
Class Query
{
    [string]$Name;
    [string]$Value;
}
Class ApiResult
{
    [object]$Value;
    [string]$Message;
    [int]$ReturnCode;
}
Class PropertyValue
{
    [Property]$Property;
    [string]$PropertyValueFilter;
    [string]$Value;
}

Class User
{
    [int]$Id;
    [string]$DisplayName;
    [string]$LoginType;
    [string]$LanId;
}

Class UserAdd
{
    [string]$DisplayName;
    [string]$LoginType;
    [string]$LanIdType;
    [string]$LanId;
    [string]$Team;
}