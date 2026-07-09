namespace OTW.Api.Models;

public class ChatLog {
    public int      LogId       { get; set; }
    public int      UserId      { get; set; }
    public string   UserMessage { get; set; } = "";
    public string   AIResponse  { get; set; } = "";
    public DateTime CreatedAt   { get; set; } = DateTime.UtcNow;
    public User     User        { get; set; } = null!;
}

