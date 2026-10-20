using DotnetScope.Core;

namespace DotnetScope.UnitTests;

public sealed class ExitCodesTests
{
    [Theory]
    [InlineData(ExitCodes.Success, 0)]
    [InlineData(ExitCodes.UnexpectedError, 1)]
    [InlineData(ExitCodes.Usage, 2)]
    [InlineData(ExitCodes.NotFound, 3)]
    [InlineData(ExitCodes.Network, 4)]
    [InlineData(ExitCodes.Integrity, 5)]
    [InlineData(ExitCodes.Conflict, 6)]
    [InlineData(ExitCodes.Privilege, 7)]
    [InlineData(ExitCodes.MissingDependency, 8)]
    [InlineData(ExitCodes.Canceled, 130)]
    public void ExitCodes_ValoresDocumentados_SaoEstaveis(int actual, int expected) =>
        Assert.Equal(expected, actual);
}
