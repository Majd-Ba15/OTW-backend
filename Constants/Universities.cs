namespace OTW.Api.Constants;

// Mirrors frontend/lib/universities.ts — same codes, domains, campus names.
// Update BOTH files together if this list changes.

public record Campus(string Name, decimal Lat, decimal Lng);

public class UniversityInfo {
    public string   Code          { get; set; } = "";
    public string   Name          { get; set; } = "";
    public string[] EmailDomains  { get; set; } = [];
    public List<Campus> Campuses  { get; set; } = [];
}

public static class Universities {
    public static readonly List<UniversityInfo> All =
    [
        new() { Code="LAU", Name="Lebanese American University", EmailDomains=["lau.edu"], Campuses=[
            new("LAU Beirut Campus", 33.8925m, 35.4772m),
            new("LAU Byblos Campus", 34.1090m, 35.6520m),
        ]},
        new() { Code="AUB", Name="American University of Beirut", EmailDomains=["aub.edu","mail.aub.edu"], Campuses=[
            new("AUB Beirut Campus", 33.9008m, 35.4823m),
        ]},
        new() { Code="LIU", Name="Lebanese International University", EmailDomains=["liu.edu.lb","students.liu.edu.lb"], Campuses=[
            new("LIU Beirut Campus", 33.8715m, 35.5100m),
            new("LIU Saida Campus", 33.5580m, 35.3870m),
            new("LIU Nabatieh Campus", 33.3780m, 35.4840m),
            new("LIU Tripoli Campus", 34.4330m, 35.8440m),
            new("LIU Bekaa Campus", 33.8460m, 35.9020m),
        ]},
        new() { Code="LU", Name="Lebanese University", EmailDomains=["ul.edu.lb","st.ul.edu.lb"], Campuses=[
            new("LU Hadath Campus", 33.8280m, 35.5310m),
            new("LU Tripoli Campus", 34.4250m, 35.8330m),
        ]},
        new() { Code="BAU", Name="Beirut Arab University", EmailDomains=["bau.edu.lb","student.bau.edu.lb"], Campuses=[
            new("BAU Beirut Campus", 33.8735m, 35.5060m),
            new("BAU Tripoli Campus", 34.4210m, 35.8290m),
            new("BAU Debbieh Campus", 33.6410m, 35.4830m),
        ]},
        new() { Code="USJ", Name="Université Saint-Joseph", EmailDomains=["usj.edu.lb","net.usj.edu.lb"], Campuses=[
            new("USJ Campus des sciences humaines", 33.8790m, 35.5150m),
        ]},
        new() { Code="NDU", Name="Notre Dame University", EmailDomains=["ndu.edu.lb"], Campuses=[
            new("NDU Zouk Mosbeh Campus", 33.9790m, 35.6180m),
        ]},
        new() { Code="USEK", Name="Holy Spirit University of Kaslik", EmailDomains=["usek.edu.lb","net.usek.edu.lb"], Campuses=[
            new("USEK Kaslik Campus", 33.9790m, 35.6280m),
        ]},
        new() { Code="OTHER", Name="Other university", EmailDomains=[], Campuses=[] },
    ];

    public static UniversityInfo? Find(string? code) =>
        string.IsNullOrEmpty(code) ? null : All.FirstOrDefault(u => u.Code == code);

    // Case-insensitive, subdomain-aware: "students.liu.edu.lb" matches "liu.edu.lb".
    // Unknown code or a university with no domains (OTHER) → always allowed.
    public static bool EmailMatches(string email, string? code) {
        var uni = Find(code);
        if (uni == null || uni.EmailDomains.Length == 0) return true;
        var at = email.LastIndexOf('@');
        if (at < 0) return false;
        var domain = email[(at + 1)..].Trim().ToLowerInvariant();
        return uni.EmailDomains.Any(d => domain == d.ToLowerInvariant() || domain.EndsWith("." + d.ToLowerInvariant()));
    }
}
