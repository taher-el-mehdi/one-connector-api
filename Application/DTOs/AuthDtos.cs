namespace DocuWareSageConnector.Application.DTOs;

public sealed class LoginRequest
{
    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;
}

public sealed class LoginResponse
{
    public required string Username { get; init; }

    public string? DisplayName { get; init; }

    public required string Token { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }
}

public sealed class AuthenticatedOperator
{
    public required Guid UserId { get; init; }

    public required string Username { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }
}

public sealed class OperatorProfile
{
    public required Guid Id { get; init; }

    public required string Username { get; init; }

    public string? DisplayName { get; init; }

    public string? Email { get; init; }

    public required bool IsActive { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public DateTimeOffset? LastLoginAt { get; init; }
}
