// ============================================================================
//  DriveLab
//  EncoderTest.cs — o teste de alinhamento do encoder na lista de testes.
//
//  DIFERENTE DE TODOS OS OUTROS, e o motivo importa: os demais testes são o APP
//  mandando força e olhando a resposta. Este é a BASE fazendo tudo — ela gira o
//  motor em malha aberta, compara o que o encoder leu com onde o rotor estava, e
//  devolve o erro pela telemetria. O app só dispara e espera.
//
//  A razão é física: para saber se o encoder mente é preciso uma referência
//  independente DELE. Com 15 pares de polos, fixar a fase elétrica trava o rotor
//  numa posição determinada pelo ferro do motor — essa é a régua, e ela só existe
//  dentro do laço de controle. Nada disso pode ser feito por USB a 100 Hz.
//
//  ⚠️ EXIGE O MOTOR DESARMADO, ao contrário dos outros. O firmware recusa se
//  estiver armado: girar em malha aberta com o controle ativo põe dois donos no
//  mesmo motor.
//
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Core.Testing;

/// <summary>Mede o quanto a leitura do encoder se afasta da posição real do rotor, e traduz isso em
/// perda de força e calor (ver EncoderHealth). Não altera a calibração: o firmware guarda o offset
/// antes de varrer e o devolve ao terminar.</summary>
public sealed class EncoderTest : IForceTest
{
    public string Id => "Encoder";

    /// <summary>Teto de espera, não duração fixa: quem manda parar é a base avisando que terminou.
    /// A varredura leva ~15 s por sentido; 60 s dá margem para um motor pesado sem deixar a tela
    /// esperando para sempre se a base não responder.</summary>
    public double DuracaoS => 60;

    /// <summary>Zero: o app não manda força nenhuma. O motor gira, mas quem o gira é a base, em
    /// malha aberta e com a corrente de calibração — bem abaixo de qualquer força de FFB.</summary>
    public double PicoDeForca => 0;

    public string PreparoKey => "ForceTest_Encoder_Prep";

    public ForceCommand ForcaEm(double t) => ForceCommand.Zero;

    /// <summary>Não é por aqui que este teste conclui: o veredito sai de EncoderHealth sobre o que a
    /// base mediu, e não das amostras de força (que são todas zero). Fica explícito para o dia em que
    /// alguém adicionar um teste novo copiando este e se perguntar por que Avaliar não faz nada.</summary>
    public ForceTestResult Avaliar(IReadOnlyList<ForceTestSample> amostras) =>
        new(false, "A base não devolveu a medição", Array.Empty<string>());
}
