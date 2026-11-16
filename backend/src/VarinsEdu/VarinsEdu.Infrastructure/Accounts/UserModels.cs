namespace VarinsEdu.Infrastructure.Accounts;

public sealed record UserListItem(
    Guid Id,
    string Username,
    bool IsActive,
    bool IsStudent,
    IReadOnlyList<string> Roles,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    bool IsLocked,
    bool IsDevice);

public sealed record UserPage(int Total, IReadOnlyList<UserListItem> Items);

public sealed record RoleItem(string Key, string Name);

public sealed record CreateStaffInput(string? Username, string? RoleKey);

public sealed record CreateDeviceInput(string? Name);

public sealed record RolesInput(IReadOnlyList<string>? RoleKeys);

public sealed record ActiveInput(bool IsActive);

// The plain-text code appears here and nowhere else: it is shown once and never stored.
public sealed record IssuedResetCode(Guid UserId, string Username, string Code, DateTimeOffset ExpiresAt);
