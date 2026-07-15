// ============================================================================
//  DriveLab
//  SmokeTest.cs — Teste de fumaça: ViewModelBase pode ser usada como base.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Studio.ViewModels;
using Xunit;

namespace DriveLab.Studio.Tests;

public class SmokeTest
{
    private sealed class SampleViewModel : ViewModelBase { }

    [Fact]
    public void ViewModelBase_Is_Usable_As_Base()
    {
        var vm = new SampleViewModel();
        Assert.IsAssignableFrom<ViewModelBase>(vm);
    }
}
