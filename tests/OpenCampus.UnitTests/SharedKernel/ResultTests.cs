using OpenCampus.SharedKernel;

namespace OpenCampus.UnitTests.SharedKernel;

public class ResultTests
{
    [Fact]
    public void Success_HasNoError()
    {
        var result = Result.Success();

        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
        result.Error.ShouldBe(Error.None);
    }

    [Fact]
    public void Failure_CarriesError()
    {
        var error = new Error("code", "message");

        var result = Result.Failure(error);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(error);
    }

    [Fact]
    public void FailureWithNoError_IsRejected()
    {
        Should.Throw<ArgumentException>(() => Result.Failure(Error.None));
    }

    [Fact]
    public void TypedSuccess_ExposesValue()
    {
        var result = Result.Success(42);

        result.Value.ShouldBe(42);
    }

    [Fact]
    public void TypedFailure_RefusesValueAccess()
    {
        var result = Result.Failure<int>(new Error("code", "message"));

        Should.Throw<InvalidOperationException>(() => result.Value);
    }
}
