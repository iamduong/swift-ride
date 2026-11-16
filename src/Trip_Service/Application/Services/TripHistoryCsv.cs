using System.Globalization;
using System.Text;
using Trip_Service.Application.Contracts.Trip;

namespace Trip_Service.Application.Services;

public static class TripHistoryCsv
{
    public static byte[] Create(IEnumerable<HistoryTripResponseContract> trips)
    {
        var csv = new StringBuilder("\uFEFF");
        csv.AppendLine("Uuid,PickupName,DestinationName,DistanceKM,DurationMin,Price,Status,CreatedAt,UpdatedAt");

        foreach (var trip in trips)
        {
            csv.Append(Escape(trip.Uuid.ToString())).Append(',')
                .Append(Escape(trip.PickupName)).Append(',')
                .Append(Escape(trip.DestinationName)).Append(',')
                .Append(Decimal(trip.DistanceKM)).Append(',')
                .Append(Decimal(trip.DurationMin)).Append(',')
                .Append(Decimal(trip.Price)).Append(',')
                .Append(Escape(trip.Status)).Append(',')
                .Append(trip.CreatedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)).Append(',')
                .AppendLine(trip.UpdatedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        }

        return Encoding.UTF8.GetBytes(csv.ToString());
    }

    private static string Decimal(decimal? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Escape(string value)
    {
        // Spreadsheet applications execute cells beginning with these characters as formulas.
        var firstNonSpace = value.TrimStart(' ');
        if (firstNonSpace.Length > 0 && "=+-@\t\r\n".Contains(firstNonSpace[0]))
        {
            value = "'" + value;
        }

        return '"' + value.Replace("\"", "\"\"") + '"';
    }
}
