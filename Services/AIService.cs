using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MailKit.Net.Smtp;
using MimeKit;
using OTW.Api.Data;
using OTW.Api.Models;
using System.Net.Http.Json;
using System.Text.Json;

namespace OTW.Api.Services;

public class AIService : IAIService {
    readonly IConfiguration _cfg;
    public AIService(IConfiguration cfg) => _cfg=cfg;
    public async Task<string> ChatAsync(string system, string message) {
        try {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _cfg["OpenAI:ApiKey"]);
            var body = new {
                model = "gpt-3.5-turbo",
                messages = new[] {
                    new { role = "system", content = system },
                    new { role = "user",   content = message }
                },
                max_tokens = 500
            };
            var res  = await http.PostAsJsonAsync("https://api.openai.com/v1/chat/completions", body);
            var json = await res.Content.ReadFromJsonAsync<JsonElement>();
            return json.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()!;
        } catch { return "I am unable to process your request right now. Please try again later."; }
    }
}

