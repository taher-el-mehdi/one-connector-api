namespace DocuWareSageConnector.Application.DTOs;

public sealed class CreateLeadRequest
{
    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string CompanyName { get; set; } = string.Empty;

    public string WorkEmail { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;

    public string Country { get; set; } = string.Empty;
}

public sealed class NewLead
{
    public required string FirstName { get; init; }

    public required string LastName { get; init; }

    public required string CompanyName { get; init; }

    public required string WorkEmail { get; init; }

    public required string PhoneNumber { get; init; }

    public required string Country { get; init; }
}

public sealed class LeadCreatedResponse
{
    public required Guid Id { get; init; }
}
