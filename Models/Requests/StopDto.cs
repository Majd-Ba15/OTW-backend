namespace OTW.Api.Models;

public record StopDto(string StopName, decimal? StopLat, decimal? StopLng, decimal StopPrice, int StopOrder);

