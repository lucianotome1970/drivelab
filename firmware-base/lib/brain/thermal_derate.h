// ============================================================================
//  DriveLab Firmware
//  thermal_derate.h — Teto de torque dinâmico: libera o PICO por um tempo, depois cai pro CONTÍNUO. Puro/host-testável.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
//
// FFB é RAJADA: o piloto precisa do PICO por segundos (zebra, contra-esterço), não por minutos. Um motor
// aguenta muito mais torque em rajada do que contínuo — o que "frita" é o I²R sustentado. Este bloco modela
// isso com um ORÇAMENTO de pico (em segundos): enquanto sobra orçamento, o teto é o PICO; conforme o torque
// alto drena o orçamento, o teto desce suave até o CONTÍNUO; com torque baixo o orçamento recarrega.
//
// É um modelo I²t simplificado e NORMALIZADO (aquecimento ∝ torque², já que torque ∝ corrente): a plena carga
// (pico) o orçamento drena a 1 s/s, então "peakHoldS" é literalmente quantos segundos de pico pleno cabem.
// Puro: sem tempo real, sem motor — o `dt` vem de fora. NÃO liga o motor; é só um teto que o engine aplica.
#pragma once

namespace drivelab {

class ThermalDerate {
public:
    // --- configuração (setada por apply_cfg a partir dos settings de hardware) ---
    float peakNm    = 0.0f;  ///< teto de pico (= teto duro de torque atual)
    float contNm    = 0.0f;  ///< teto contínuo sustentável (fração do pico)
    float peakHoldS = 0.0f;  ///< segundos de pico PLENO antes de cair pro contínuo (0 = desligado)

    /// Configura os tetos. Na 1ª vez semeia o orçamento CHEIO (parte "frio"); depois só clampa ao novo
    /// teto de tempo (preserva o estado térmico entre reconfigs). Chamada por apply_cfg.
    void configure(float peak, float cont, float holdSeconds) {
        peakNm = peak; contNm = cont; peakHoldS = holdSeconds;
        if (!m_init) { m_budgetS = peakHoldS; m_init = true; }
        else {
            if (m_budgetS > peakHoldS) m_budgetS = peakHoldS;
            if (m_budgetS < 0.0f)      m_budgetS = 0.0f;
        }
    }

    /// Ligado só quando faz sentido: há janela de pico (holdS>0) e o pico é de fato maior que o contínuo.
    bool enabled() const { return peakHoldS > 0.0f && contNm > 0.0f && peakNm > contNm; }

    /// Fração do orçamento de pico restante (1 = frio/pico pleno disponível, 0 = só contínuo). P/ telemetria.
    float budgetFraction() const {
        if (!enabled()) return 1.0f;
        return m_budgetS / peakHoldS;
    }

    /// Um tick: recebe a MAGNITUDE do torque pedido e `dt` (s). Retorna a MAGNITUDE de torque PERMITIDA agora
    /// (o engine clampa ±retorno). Atualiza o orçamento pelo torque de fato ENTREGUE (min(pedido, permitido)).
    /// Desligado → retorna um teto "infinito" (clamp vira no-op) e não mexe no orçamento.
    float update(float demandMag, float dt) {
        if (!enabled()) return 1.0e9f;              // passthrough — o teto duro do engine continua valendo
        if (!m_init) { m_budgetS = peakHoldS; m_init = true; }

        const float headroom = m_budgetS / peakHoldS;                 // 1..0
        const float allowed  = contNm + (peakNm - contNm) * headroom; // pico (frio) → contínuo (sem orçamento)
        const float applied  = demandMag < allowed ? demandMag : allowed;

        // Aquecimento ∝ torque²; normalizado pelo "excesso" a pleno pico (drena 1 s/s a pico pleno).
        const float fullOver = peakNm * peakNm - contNm * contNm;     // >0 (enabled)
        const float over     = applied * applied - contNm * contNm;   // >0 aquece, <0 esfria
        m_budgetS -= dt * (over / fullOver);
        if (m_budgetS < 0.0f)          m_budgetS = 0.0f;
        else if (m_budgetS > peakHoldS) m_budgetS = peakHoldS;
        return allowed;
    }

    void reset() { m_budgetS = peakHoldS; m_init = true; }

private:
    float m_budgetS = 0.0f;   ///< orçamento de pico restante (s), em [0, peakHoldS]
    bool  m_init    = false;  ///< orçamento já semeado (evita começar "vazio" antes do 1º configure/update)
};

}  // namespace drivelab
