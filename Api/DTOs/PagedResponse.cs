namespace Api.DTOs;

public class PagedResponse<T>
{
    public List<T> Items    { get; set; } = new();
    public int     Page     { get; set; }
    public int     PageSize { get; set; }
    public int     Total    { get; set; }
}