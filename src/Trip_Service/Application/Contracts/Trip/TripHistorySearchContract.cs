using Trip_Service.Domain;

namespace Trip_Service.Application.Contracts.Trip;

public sealed class TripHistorySearchContract
{
    public TripStatus? Status { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;

    public void Validate()
    {
        if (Page < 1 || PageSize is < 1 or > 100)
        {
            throw new ArgumentException("Page phải lớn hơn 0 và PageSize phải nằm trong khoảng 1–100.");
        }

        if ((long)(Page - 1) * PageSize > int.MaxValue)
        {
            throw new ArgumentException("Trang được yêu cầu vượt quá giới hạn.");
        }

        if (Status is { } status && !Enum.IsDefined(status))
        {
            throw new ArgumentException("Trạng thái chuyến đi không hợp lệ.");
        }

        if (From > To)
        {
            throw new ArgumentException("From phải trước hoặc bằng To.");
        }

        if (Search?.Length > 100)
        {
            throw new ArgumentException("Từ khóa tìm kiếm không được dài quá 100 ký tự.");
        }

        Search = Search?.Trim();
    }
}

public sealed class TripHistoryPageContract
{
    public required IReadOnlyList<HistoryTripResponseContract> Items { get; init; }
    public int TotalItems { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalPages => TotalItems / PageSize + (TotalItems % PageSize == 0 ? 0 : 1);
}
