using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Orleans.Surrogates;

[RegisterConverter]
public sealed class
    TagWriteReservationSurrogateConverter : IConverter<TagWriteReservation, TagWriteReservationSurrogate>
{
    public TagWriteReservation ConvertFromSurrogate(in TagWriteReservationSurrogate surrogate) =>
        new(surrogate.ReservationCode, surrogate.ExpiredUTC, surrogate.Tag);

    public TagWriteReservationSurrogate ConvertToSurrogate(in TagWriteReservation value) =>
        new()
        {
            ReservationCode = value.ReservationCode,
            ExpiredUTC = value.ExpiredUTC,
            Tag = value.Tag
        };
}
