namespace Tools.PropertyValueCreationCLI
{
    internal interface IPropertyCreation
    {
        void InsertProperty(string propertyName, bool secure, string updatedBy);
    }
}