namespace RenoDXCommander.Models;

public record OAuthTokenData(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset Expiry);
