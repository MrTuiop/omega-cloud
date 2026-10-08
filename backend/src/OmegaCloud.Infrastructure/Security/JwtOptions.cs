namespace OmegaCloud.Infrastructure.Security;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Secret { get; set; } = "omega_cloud_master_secret_key_super_secure_32_bytes_min";
    public string Issuer { get; set; } = "OmegaCloudMaster";
    public string Audience { get; set; } = "OmegaCloudClients";
    public int AccessTokenExpirationMinutes { get; set; } = 15;
    public int RefreshTokenExpirationDays { get; set; } = 30;
}
