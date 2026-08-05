/*
 * test_power_button.cpp — testes host da máquina de estados do soft-power (PowerButton).
 * Autor: Luciano Tomé
 * Licença: MIT
 */
#include "power_button.h"
#include <cassert>
#include <cstdio>

int main() {
    // Boot: latch ligado, sem shutdown.
    {
        PowerButton pb(2000, 200);
        pb.step(false, false, 0);
        assert(pb.powerEnable());
        assert(!pb.shuttingDown());
        assert(pb.state() == PowerButtonState::Running);
    }
    // Toque curto (release antes do hold): ignorado, segue ligado.
    {
        PowerButton pb(2000, 200);
        pb.step(true, false, 0);      // botão desce
        pb.step(true, false, 500);    // ainda segurando (< 2000)
        pb.step(false, false, 600);   // soltou
        pb.step(false, false, 5000);  // muito tempo depois
        assert(pb.powerEnable());
        assert(!pb.shuttingDown());
        assert(pb.state() == PowerButtonState::Running);
    }
    // Segurar >= holdMs: entra em shutdown, mas ainda com energia (contator não abriu).
    {
        PowerButton pb(2000, 200);
        pb.step(true, false, 0);      // desce
        pb.step(true, false, 2000);   // segurou 2000ms → shutdown
        assert(pb.shuttingDown());
        assert(pb.state() == PowerButtonState::ShuttingDown);
        assert(pb.powerEnable());     // NÃO corta antes do contator abrir
    }
    // Sequência completa: shutdown → contator abre → +delay → corta energia.
    {
        PowerButton pb(2000, 200);
        pb.step(true, false, 0);
        pb.step(true, false, 2000);              // shutdown
        pb.step(true, true, 2100);               // contator abriu → Cutting
        assert(pb.state() == PowerButtonState::Cutting);
        assert(pb.powerEnable());                // ainda ligado (delay não passou)
        pb.step(true, true, 2300);               // +200ms → Cut
        assert(pb.state() == PowerButtonState::Cut);
        assert(!pb.powerEnable());               // AGORA corta
        assert(pb.shuttingDown());
    }
    // powerEnable NÃO cai enquanto o contator não abre, por mais que se segure.
    {
        PowerButton pb(2000, 200);
        pb.step(true, false, 0);
        pb.step(true, false, 2000);   // shutdown
        pb.step(true, false, 9000);   // contator NUNCA abriu
        assert(pb.powerEnable());     // energia mantida (evita arco)
        assert(pb.state() == PowerButtonState::ShuttingDown);
    }
    // reset() volta ao boot.
    {
        PowerButton pb(2000, 200);
        pb.step(true, false, 0);
        pb.step(true, false, 2000);
        pb.reset();
        assert(pb.state() == PowerButtonState::Running);
        assert(pb.powerEnable());
        assert(!pb.shuttingDown());
    }
    // Wrap-safe: nowMs perto do overflow de uint32 não dispara shutdown falso.
    {
        PowerButton pb(2000, 200);
        uint32_t t0 = 0xFFFFFF00u;    // ~perto do wrap
        pb.step(true, false, t0);
        pb.step(true, false, t0 + 500); // wrap acontece no meio; 500ms < 2000 → sem shutdown
        assert(!pb.shuttingDown());
        pb.step(true, false, t0 + 2000); // 2000ms decorridos (com wrap) → shutdown
        assert(pb.shuttingDown());
    }
    printf("test_power_button OK\n");
    return 0;
}
