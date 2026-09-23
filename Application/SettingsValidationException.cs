namespace DocuWareSageConnector.Application;

public sealed class SettingsValidationException : Exception
{
    public SettingsValidationException(string code) : base(code)
    {
    }
}
