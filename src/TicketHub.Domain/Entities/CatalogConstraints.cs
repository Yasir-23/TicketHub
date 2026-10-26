namespace TicketHub.Domain.Entities;

public static class VenueConstraints
{
    public const int NameMaxLength = 200;
    public const int CityMaxLength = 100;
    public const int AddressMaxLength = 500;
}

public static class SectionConstraints
{
    public const int NameMaxLength = 100;
    public const int RowNumberMaxLength = 10;
    public const int MaxRowsPerSection = 200;
    public const int MaxSeatsPerRow = 500;
}

public static class EventConstraints
{
    public const int TitleMaxLength = 200;
    public const int DescriptionMaxLength = 4000;
}
