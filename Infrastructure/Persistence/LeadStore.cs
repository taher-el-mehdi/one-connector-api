using DocuWareSageConnector.Application.DTOs;
using DocuWareSageConnector.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using MySqlConnector;

namespace DocuWareSageConnector.Infrastructure.Persistence;

public sealed class LeadStore : ILeadStore
{
    private readonly string _connectionString;

    public LeadStore(IConfiguration configuration)
    {
        _connectionString = ConnectorStoreConnections.RequireConnectionString(configuration);
    }

    public async Task<Guid> AddAsync(NewLead lead, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand(
            """
            INSERT INTO leads (
                id,
                first_name,
                last_name,
                company_name,
                work_email,
                phone_number,
                country,
                created_at
            ) VALUES (
                @id,
                @firstName,
                @lastName,
                @companyName,
                @workEmail,
                @phoneNumber,
                @country,
                @createdAt
            )
            """,
            connection);
        command.Parameters.AddWithValue("@id", id.ToString("D"));
        command.Parameters.AddWithValue("@firstName", lead.FirstName);
        command.Parameters.AddWithValue("@lastName", lead.LastName);
        command.Parameters.AddWithValue("@companyName", lead.CompanyName);
        command.Parameters.AddWithValue("@workEmail", lead.WorkEmail);
        command.Parameters.AddWithValue("@phoneNumber", lead.PhoneNumber);
        command.Parameters.AddWithValue("@country", lead.Country);
        command.Parameters.AddWithValue("@createdAt", DateTime.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return id;
    }
}
