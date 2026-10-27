using System.Text.Json.Serialization;

namespace MIXO6.Models;

public sealed class ApiUser
{
    public string Id { get; set; } = "";
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string Avatar { get; set; } = "◈";
    public string MainGame { get; set; } = "";
    public bool IsVerified { get; set; }
}

public sealed class LobbyDto
{
    public long Id { get; set; }
    public string Game { get; set; } = "";
    [JsonPropertyName("server_label")] public string ServerLabel { get; set; } = "";
    public string Description { get; set; } = "";
    public string Status { get; set; } = "open";
    [JsonPropertyName("creator_username")] public string CreatorUsername { get; set; } = "";
    [JsonPropertyName("creator_avatar")] public string CreatorAvatar { get; set; } = "◈";
    [JsonPropertyName("creator_online")] public bool CreatorOnline { get; set; }
    [JsonPropertyName("member_count")] public int MemberCount { get; set; }
    [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; set; }
}

public sealed class JoinLobbyResult
{
    public long LobbyId { get; set; }
    public string ServerAddress { get; set; } = "";
    public string CreatorUsername { get; set; } = "";
    public string Game { get; set; } = "";
    public string Description { get; set; } = "";
}

public sealed class CreateLobbyRequest
{
    public string Game { get; set; } = "";
    public string ServerAddress { get; set; } = "";
    public string ServerLabel { get; set; } = "";
    public string Description { get; set; } = "";
}

public sealed class PublicProfile
{
    public string Username { get; set; } = "";
    public string Avatar { get; set; } = "◈";
    [JsonPropertyName("main_game")] public string MainGame { get; set; } = "";
    public bool Online { get; set; }
    [JsonPropertyName("last_seen_at")] public DateTimeOffset? LastSeenAt { get; set; }
}
