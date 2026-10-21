namespace OTW.Api.Models;

// Brevo (formerly Sendinblue) transactional email over HTTPS — used instead of
// SMTP because hosts like Render block outbound SMTP ports. ApiKey comes from
// the Brevo__ApiKey environment variable in production.
public class BrevoSettings {
    public string ApiKey { get; set; } = "";
}
