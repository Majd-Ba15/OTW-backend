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

public class NotificationService : INotificationService {
    readonly AppDbContext _db;
    public NotificationService(AppDbContext db) => _db=db;
    public async Task CreateAsync(int userId,string title,string body,string type,int? relatedId=null) {
        _db.Notifications.Add(new Notification{UserId=userId,Title=title,Body=body,Type=type,RelatedId=relatedId});
        await _db.SaveChangesAsync();
    }
}

