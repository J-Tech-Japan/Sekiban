using System;
namespace FeatureCheck.Test;

public class RecordSpec
{
    // public void GetITest<T>(T test) where T : ITest, IEquatable<T>
    // {
    // }
    // public void Usage()
    // {
    //     GetITest(new ClassImplemented());
    //     GetITest(new RecordClassImplemented(string.Empty, 0));
    //     GetITest(new RecordStructImplemented(string.Empty, 0));
    // }
    // public interface ITest;
    // public class ClassImplemented : ITest
    // {
    //     public string Value { get; } = string.Empty;
    //     public int IntValue { get; } = 0;
    // }
    // public record RecordClassImplemented(string Value, int IntValue) : ITest;
    // public record struct RecordStructImplemented(string Value, int IntValue) : ITest;

    public interface IEventPayloadTest<T> where T : IEquatable<T>;
    // we want to restrict the implementation of IEventPayloadTest to records only. This cause a compile error.
    // public class EventTestImplemented : IEventPayloadTest<EventTestImplemented>
    // {
    //     public string Value { get; } = string.Empty;
    //     public int IntValue { get; } = 0;
    // }
    public record EventTestRecordClassImplemented(string Value, int IntValue)
        : IEventPayloadTest<EventTestRecordClassImplemented>;
    public record EventTestRecordClassImplemented2 : IEventPayloadTest<EventTestRecordClassImplemented2>
    {
        public string Value { get; init; } = string.Empty;
        public int IntValue { get; init; } = 0;
    }
    public record struct EventTestRecordStructImplemented(string Value, int IntValue)
        : IEventPayloadTest<EventTestRecordStructImplemented>;
}
