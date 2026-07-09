namespace OTW.Api.Models;

public class FavouriteRoute {
    public int      FavouriteId  { get; set; }
    public int      UserId       { get; set; }
    public string   FromLocation { get; set; } = "";
    public string   ToLocation   { get; set; } = "";
    public int      UseCount     { get; set; } = 1;
    public DateTime CreatedAt    { get; set; } = DateTime.UtcNow;
    public User     User         { get; set; } = null!;
}

