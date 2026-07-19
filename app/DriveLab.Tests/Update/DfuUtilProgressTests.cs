// ============================================================================
//  DriveLab
//  DfuUtilProgressTests.cs — Testes de DfuUtilProgress.Parse contra linhas
//  reais de saída do dfu-util 0.11 (banner, progresso, múltiplas atualizações
//  coladas por '\r', conclusão e erro).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Update;

namespace DriveLab.Tests.Update;

public class DfuUtilProgressTests
{
    [Theory]
    [InlineData("Download\t[=========                 ]  38%                             ", 0.38)]
    [InlineData("Download\t[=========================] 100%        42532 bytes", 1.0)]
    [InlineData("Download\t[=                          ]   1%                             ", 0.01)]
    [InlineData("Download\t[                           ]   0%                             ", 0.0)]
    public void Parse_Extracts_Fraction_From_Real_Dfu_Util_Progress_Lines(string line, double expected)
    {
        var result = DfuUtilProgress.Parse(line);

        Assert.NotNull(result);
        Assert.Equal(expected, result!.Value, 3);
    }

    [Fact]
    public void Parse_Takes_Last_Percentage_When_Multiple_Updates_Joined_By_CarriageReturn()
    {
        // dfu-util rewrites its progress line in-place with '\r'; if our reader hands us a
        // raw chunk with several such updates glued together instead of one per call, only
        // the LAST one reflects the current state.
        var chunk = "Download\t[====                       ]  12%\r" +
                    "Download\t[==========                 ]  38%\r" +
                    "Download\t[===============            ]  55%";

        var result = DfuUtilProgress.Parse(chunk);

        Assert.NotNull(result);
        Assert.Equal(0.55, result!.Value, 3);
    }

    [Theory]
    [InlineData("Opening DFU capable USB device...")]
    [InlineData("ID 0483:df11")]
    [InlineData("Run-time device DFU version 011a")]
    [InlineData("Claiming USB DFU Interface...")]
    [InlineData("Setting Alternate Setting #0 ...")]
    [InlineData("Determining device status...")]
    [InlineData("DFU state(2) = dfuIDLE, status(0) = No error condition is present")]
    [InlineData("DFU mode device DFU version 011a")]
    [InlineData("Copying data from PC to DFU device")]
    [InlineData("Download done.")]
    [InlineData("File downloaded successfully")]
    [InlineData("Transitioning to dfuMANIFEST state")]
    [InlineData("")]
    [InlineData(null)]
    public void Parse_Returns_Null_For_NonProgress_Lines(string? line)
    {
        Assert.Null(DfuUtilProgress.Parse(line));
    }

    [Fact]
    public void Parse_Clamps_Out_Of_Range_Percent_Defensively()
    {
        Assert.Equal(1.0, DfuUtilProgress.Parse("garbled [===] 150%")!.Value, 3);
    }
}
