using System.Text.RegularExpressions;
using DocuWareSageConnector.Application.DTOs;

namespace DocuWareSageConnector.Application.Leads;

public static partial class LeadRules
{
    public static NewLead? Normalize(CreateLeadRequest? request)
    {
        if (request is null)
        {
            return null;
        }

        var firstName = (request.FirstName ?? string.Empty).Trim();
        var lastName = (request.LastName ?? string.Empty).Trim();
        var companyName = (request.CompanyName ?? string.Empty).Trim();
        var workEmail = (request.WorkEmail ?? string.Empty).Trim();
        var phoneNumber = (request.PhoneNumber ?? string.Empty).Trim();
        var country = (request.Country ?? string.Empty).Trim().ToUpperInvariant();

        if (firstName.Length is < 1 or > 80
            || lastName.Length is < 1 or > 80
            || companyName.Length is < 1 or > 160
            || workEmail.Length is < 1 or > 254
            || phoneNumber.Length is < 1 or > 40
            || country.Length != 2
            || country.Any(character => character is < 'A' or > 'Z')
            || !EmailPattern().IsMatch(workEmail)
            || phoneNumber.Count(char.IsDigit) < 7)
        {
            return null;
        }

        return new NewLead
        {
            FirstName = firstName,
            LastName = lastName,
            CompanyName = companyName,
            WorkEmail = workEmail,
            PhoneNumber = phoneNumber,
            Country = country
        };
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();
}
