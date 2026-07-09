using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OTW.Api.Data;
using OTW.Api.Models;
using OTW.Api.Services;

namespace OTW.Api.Controllers;

[ApiController][Route("api/reports")][Authorize]
public class ReportsController : ControllerBase {
    readonly AppDbContext _db;
    public ReportsController(AppDbContext db) => _db=db;
    int Me => int.Parse(User.FindFirst("userId")!.Value);

    [HttpPost]
    public async Task<IActionResult> File([FromBody] ReportRequest req) {
        var r=new Report{ReportedBy=Me,ReportedUser=req.ReportedUser,RideId=req.RideId,Type=req.Type,Statement=req.Statement,Status="Open"};
        _db.Reports.Add(r); await _db.SaveChangesAsync();
        return Ok(new{reportId=r.ReportId});
    }

    [HttpGet("mine")]
    public async Task<IActionResult> GetMine() =>
        Ok(await _db.Reports.Where(r=>r.ReportedBy==Me).OrderByDescending(r=>r.CreatedAt).ToListAsync());
}
