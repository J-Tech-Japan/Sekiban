namespace DcbLib.Tags;

public record TagWriteReservation(
    string ReservationCode,
    string ExpiredUTC,
    string Tag
);