namespace OTW.Api.Models;

public class Message {
    public int      MessageId   { get; set; }
    public int      RideId      { get; set; }
    public int      SenderId    { get; set; }
    public int?     ReceiverId  { get; set; }
    public string   Content     { get; set; } = "";
    public bool     IsBroadcast { get; set; }
    public bool     IsRead      { get; set; }
    public DateTime SentAt      { get; set; } = DateTime.UtcNow;
    public Ride     Ride        { get; set; } = null!;
    public User     Sender      { get; set; } = null!;
}

