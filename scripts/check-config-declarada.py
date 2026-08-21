#!/usr/bin/env python3
# ============================================================================
#  DriveLab
#  check-config-declarada.py — Acusa campo de configuracao do ODrive de que o
#  firmware DEPENDE e que ninguem define.
#
#  POR QUE EXISTE: e o espelho do check-orphan-settings.py. Aquele pega "o app
#  transporta e o firmware ignora"; este pega "o firmware depende e ninguem
#  declara" — o campo fica no valor que estiver na NVM da placa.
#
#  Numa placa nossa isso NUNCA aparece: meses de bancada deixaram na NVM algo
#  que funciona. Numa placa zerada o campo cai no padrao do ODrive e o defeito
#  nasce pronto. Foi a causa de CINCO dos seis defeitos de 2026-08-14:
#
#    · is_calibrated_ herdado         -> a base nunca arma (149 tentativas)
#    · curva sem blob                 -> entrega 40% da forca
#    · max_regen_current = 0          -> chopper aciona com o volante parado
#    · dc_max_negative_current = -0,1 -> desarma ao GIRAR o volante
#
#  E o motivo de fundo e que os padroes do ODrive foram escolhidos para um
#  motor ACIONADO, tipo braco robotico. Num volante o motor e RETROACIONADO por
#  uma pessoa o tempo todo — regeneracao nao e excecao, e cada curva. Por isso
#  todo padrao dele merece uma decisao explicita nossa, e nao so os que ja
#  mordemos.
#
#  O QUE ESTE SCRIPT COBRA: para cada campo, uma DECISAO com PROCEDENCIA — ou o
#  firmware o define, ou ele esta na lista abaixo dizendo de onde veio a certeza.
#  Nao cobra que tudo seja definido; cobra que nada fique sem decisao, e que
#  decisao fraca fique VISIVEL em vez de enterrada num comentario.
#
#  Autor: Luciano Tome <lucianotome1970@gmail.com>
#  Copyright (c) 2026 Luciano Tome — Licenca MIT
# ============================================================================
import re
import sys
from pathlib import Path

RAIZ = Path(__file__).resolve().parent.parent
FW = RAIZ / "firmware-base"
VENDOR = FW / "vendor" / "odrive-fw" / "MotorControl"

# Onde o firmware declara a configuracao da placa. Um campo definido em qualquer
# um destes conta como decidido.
FONTES = [FW / "src" / "motor_link.cpp"]

# As structs conferidas, e por qual prefixo o firmware chega nelas.
ALVOS = [
    ("Motor::Config_t",   VENDOR / "motor.hpp",   r"motor_\.config_\."),
    ("Encoder::Config_t", VENDOR / "encoder.hpp", r"encoder_\.config_\."),
    # Entrou em 14/08/2026, depois de esta struct derrubar a base tres vezes num dia.
    # `enable_overspeed_error` mora aqui: nos desligamos o clamp de velocidade e o ERRO
    # ficou ligado, comparando com um `vel_limit` que mandamos ignorar — o motor desarmava
    # a 6 voltas/s, dentro do que ele alcanca. Era exatamente o tipo de campo que este
    # script existe para nao deixar decidir sozinho, e ele nao estava sendo conferido.
    ("Controller::Config_t", VENDOR / "controller.hpp", r"controller_\.config_\."),
]

# ---------------------------------------------------------------------------
# PROCEDENCIA — de onde vem a certeza sobre um campo herdado.
#
# A primeira versao deste script cobrava que existisse uma razao, e uma razao
# fraca satisfazia o teste igual a uma medicao. Esse e exatamente o raciocinio
# que produziu os defeitos de placa zerada: `dc_max_negative_current = -0,1 A`
# tambem PARECIA razoavel para quem lia sem girar o volante.
# ---------------------------------------------------------------------------
NAO_SE_APLICA = "NAO_SE_APLICA"  # hardware que nao temos, ou campo morto. Risco zero.
SAIDA_CALIB   = "SAIDA_CALIB"    # e RESULTADO, nao entrada. Escrever descarta a medicao.
MEDIDO        = "MEDIDO"         # conferido NA BANCADA, com o efeito observado.
A_VERIFICAR   = "A_VERIFICAR"    # julgamento por leitura, sem teste. E DIVIDA.
PENDENTE      = "PENDENTE"       # sai da lista quando um trabalho ja previsto for feito.
# O valor foi decidido, mas EDITANDO O DEFAULT dentro de vendor/ em vez de atribuir em
# motor_link.cpp. Nao e o mesmo que herdar sem olhar — tem razao registrada la — mas espalha
# as decisoes por dois lugares, e quem procura "o que mudamos" no motor_link nao encontra.
# Existe para tornar essa divida CONTAVEL: cada linha destas e uma decisao a repatriar.
EDITADO_VENDOR = "EDITADO_VENDOR"

# ---------------------------------------------------------------------------
# HERDADOS DE PROPOSITO. Cada linha e uma decisao registrada, nao uma permissao.
# ---------------------------------------------------------------------------
HERDADOS = {
    # --- Nao se aplicam a este hardware -----------------------------------
    "acim_gain_min_flux":        (NAO_SE_APLICA, "motor de inducao; o nosso e PMSM"),
    "acim_autoflux_min_Id":      (NAO_SE_APLICA, "motor de inducao"),
    "acim_autoflux_enable":      (NAO_SE_APLICA, "motor de inducao"),
    "acim_autoflux_attack_gain": (NAO_SE_APLICA, "motor de inducao"),
    "acim_autoflux_decay_gain":  (NAO_SE_APLICA, "motor de inducao"),
    "hall_polarity":             (NAO_SE_APLICA, "sensor Hall; usamos ABZ ou SPI absoluto"),
    "hall_polarity_calibrated":  (NAO_SE_APLICA, "sensor Hall"),
    "ignore_illegal_hall_state": (NAO_SE_APLICA, "sensor Hall"),
    "sincos_gpio_pin_sin":       (NAO_SE_APLICA, "encoder sin/cos analogico; nao suportado"),
    "sincos_gpio_pin_cos":       (NAO_SE_APLICA, "encoder sin/cos analogico; nao suportado"),
    "inverter_temp_limit_lower": (NAO_SE_APLICA, "campo MORTO no v0.5.6 (declarado e nunca lido). "
                                                 "O limite termico real vai em fet_thermistors[0]"),
    "inverter_temp_limit_upper": (NAO_SE_APLICA, "campo MORTO no v0.5.6; ver acima"),

    # --- SAIDAS da calibracao, nao entradas -------------------------------
    # --- MEDIDO na bancada ------------------------------------------------
    "R_wL_FF_enable": (MEDIDO, "desligado. Ligado e conferido por leitura na bancada em 14/08 e "
                               "NAO mudou o ruido — mantido no padrao"),
    "bEMF_FF_enable": (MEDIDO, "desligado; mesmo teste, mesmo resultado"),
    "current_control_deadband": (MEDIDO, "0,1 A. Testado zerado na bancada em 14/08 e nao mudou o "
                                         "ruido; sem motivo para divergir do padrao"),

    # --- PENDENTE: sai daqui quando o trabalho previsto acontecer ---------
    "index_offset": (PENDENTE, "0 enquanto o indice Z nao esta em uso. Sai desta lista quando o "
                               "centro no boot for ligado"),
    "abs_spi_cs_gpio_pin": (PENDENTE, "so vale em modo SPI absoluto, que ainda nao tem driver. "
                                      "Sai desta lista junto com o MT6701"),

    # --- A_VERIFICAR: julgamento por LEITURA, sem teste ---------------------
    # Estes sao a divida. Cada um e uma hipotese razoavel que ninguem exercitou —
    # e o -0,1 A tambem era razoavel. Ao passar por qualquer um na bancada,
    # promova para MEDIDO com o que foi observado.
    "torque_lim": (A_VERIFICAR, "infinito. O teto de torque e nosso, no ffb_model, onde ele conhece "
                                "a curva e o clipping. Dois tetos em camadas diferentes brigam"),
    "I_bus_hard_min": (A_VERIFICAR, "sem limite. Quem protege o barramento e o par "
                                    "dc_max_negative_current + rampa de sobretensao, que declaramos"),
    "I_bus_hard_max": (A_VERIFICAR, "sem limite; mesmo motivo do I_bus_hard_min"),
    "I_leak_max": (A_VERIFICAR, "0,1 A. Detecta fuga de corrente; nunca disparou nesta bancada, mas "
                                "tambem nunca foi exercitado de proposito"),
    "dc_calib_tau": (A_VERIFICAR, "0,2 s. Constante do zero do sensor de corrente. Mexer no caminho "
                                  "do ADC ja custou uma sessao (18 A com o volante parado)"),
    "enable_phase_interpolation": (A_VERIFICAR, "ligado. Interpola posicao entre contagens pela "
                                                "velocidade; deve ser o que tira o degrau em giro "
                                                "lento, mas nao foi testado desligado"),

    # =========================================================================
    # Controller::Config_t
    # =========================================================================
    # A maioria nao se aplica por UMA razao so, e vale dizer de uma vez: rodamos em
    # CONTROL_MODE_TORQUE_CONTROL com INPUT_MODE_PASSTHROUGH. Um volante FFB recebe TORQUE
    # pronto do jogo; malha de posicao e de velocidade nao entram no caminho. Todo campo que
    # so alimenta essas malhas esta morto aqui — nao por escolha de tuning, mas porque o
    # codigo que os le nao executa neste modo.

    # --- So valem em POSITION/VELOCITY_CONTROL, que nao usamos -------------
    "pos_gain":             (NAO_SE_APLICA, "malha de posicao; rodamos em torque"),
    "vel_gain":             (NAO_SE_APLICA, "malha de velocidade. Tambem entra no clamp do "
                                            "enable_torque_mode_vel_limit, que esta desligado"),
    "vel_integrator_gain":  (NAO_SE_APLICA, "malha de velocidade"),
    "vel_integrator_limit": (NAO_SE_APLICA, "clamp do integrador da malha de velocidade"),
    "inertia":              (NAO_SE_APLICA, "feed-forward de aceleracao das malhas de pos/vel"),

    # --- So valem em outros INPUT_MODE ------------------------------------
    "vel_ramp_rate":         (NAO_SE_APLICA, "INPUT_MODE_VEL_RAMP; usamos PASSTHROUGH"),
    "torque_ramp_rate":      (NAO_SE_APLICA, "INPUT_MODE_TORQUE_RAMP. ⚠️ NAO confundir com rampa "
                                             "de forca: a nossa vive no ffb_model, nao aqui"),
    "input_filter_bandwidth": (NAO_SE_APLICA, "INPUT_MODE_POS_FILTER"),

    # --- Hardware ou recurso que nao temos --------------------------------
    "circular_setpoints":       (NAO_SE_APLICA, "setpoint de posicao circular; modo torque"),
    "circular_setpoint_range":  (NAO_SE_APLICA, "idem"),
    "steps_per_circular_range": (NAO_SE_APLICA, "interface step/dir, que nao expomos"),
    "homing_speed":             (NAO_SE_APLICA, "homing por fim de curso fisico; nao temos "
                                                "endstop. Nosso centro e por software"),
    "gain_scheduling_width":    (NAO_SE_APLICA, "so com enable_gain_scheduling"),
    "enable_gain_scheduling":   (NAO_SE_APLICA, "desligado; agenda ganhos da malha de posicao"),
    "axis_to_mirror":           (NAO_SE_APLICA, "espelhar um segundo eixo; placa de 1 eixo"),
    "mirror_ratio":             (NAO_SE_APLICA, "idem"),
    "torque_mirror_ratio":      (NAO_SE_APLICA, "idem"),
    "load_encoder_axis":        (NAO_SE_APLICA, "segundo encoder, na carga; temos um so"),

    # --- MEDIDO na bancada ------------------------------------------------
    "enable_overspeed_error": (MEDIDO, "LIGADO de proposito, e a decisao e de 14/08/2026. Foi ele "
                                       "que desarmou o motor a 6 voltas/s, mas desligar levaria "
                                       "junto o outro ramo do mesmo `if`, que detecta estimativa "
                                       "de velocidade invalida. Em vez disso subimos o vel_limit "
                                       "para 25: o erro vira ultimo recurso a 30 voltas/s, muito "
                                       "alem do que a mecanica alcanca, e a nossa guarda de "
                                       "sobrevelocidade — que exige persistencia — age primeiro"),

    # --- EDITADO_VENDOR: decidido, mas no default dentro de vendor/ --------
    "enable_torque_mode_vel_limit": (EDITADO_VENDOR, "false (stock true). O clamp "
                                     "`(vel_limit - vel) * vel_gain` cortava torque em modo TORQUE "
                                     "mesmo com o motor parado. Razao registrada no controller.hpp"),

    # --- A_VERIFICAR: julgamento por LEITURA, sem teste ---------------------
    "vel_limit_tolerance": (A_VERIFICAR, "1,2, o padrao. Multiplica o vel_limit no disparo do "
                                         "overspeed: mexemos no vel_limit e deixamos este quieto, "
                                         "porque dois fatores para o mesmo teto so confundem"),
    "mechanical_power_bandwidth": (A_VERIFICAR, "20 rad/s, o padrao. Filtro da potencia que "
                                                "alimenta a deteccao de spinout"),
    "electrical_power_bandwidth": (A_VERIFICAR, "20 rad/s, o padrao; par do de cima"),
}

PROCEDENCIAS = (NAO_SE_APLICA, SAIDA_CALIB, MEDIDO, PENDENTE, EDITADO_VENDOR, A_VERIFICAR)


def campos_da_struct(caminho: Path, nome: str) -> list[str]:
    """Os campos escalares de uma `struct Config_t`, na ordem em que aparecem."""
    texto = caminho.read_text(encoding="utf-8", errors="replace")
    ini = texto.find("struct Config_t")
    if ini < 0:
        sys.exit(f"[ERRO] nao achei 'struct Config_t' em {caminho}")
    fim = texto.find("};", ini)
    corpo = texto[ini:fim]
    tipos = r"bool|float|int32_t|uint32_t|uint8_t|uint16_t|int"
    return re.findall(rf"^\s*(?:{tipos})\s+(\w+)\s*(?:=|;)", corpo, re.M)


def definidos_no_firmware(prefixo: str) -> set[str]:
    """Campos que o firmware ATRIBUI (`x.config_.campo =`), ignorando comparacoes."""
    achados = set()
    for f in FONTES:
        texto = f.read_text(encoding="utf-8", errors="replace")
        # `== ` fica de fora: comparar nao e declarar.
        for m in re.finditer(prefixo + r"(\w+)\s*=(?!=)", texto):
            achados.add(m.group(1))
    return achados


def main() -> int:
    sem_decisao: list[tuple[str, str]] = []
    contraditorios: list[str] = []
    n_definidos = 0
    total = 0

    for nome, cabecalho, prefixo in ALVOS:
        campos = campos_da_struct(cabecalho, nome)
        definidos = definidos_no_firmware(prefixo)
        for c in campos:
            total += 1
            if c in definidos:
                n_definidos += 1
                # Estar nas DUAS listas nao e redundancia inofensiva: a lista
                # afirma "herdado" sobre um campo que o firmware define, e quem
                # ler acredita nela em vez de no codigo.
                if c in HERDADOS:
                    contraditorios.append(f"{nome}::{c}")
            elif c not in HERDADOS:
                sem_decisao.append((nome, c))

    por_proc: dict[str, list[str]] = {}
    for campo, (proc, _) in HERDADOS.items():
        por_proc.setdefault(proc, []).append(campo)

    print(f"campos de configuracao: {total} | definidos no firmware: {n_definidos} | "
          f"herdados: {total - n_definidos - len(sem_decisao)} | sem decisao: {len(sem_decisao)}")
    print("  por procedencia: " + " | ".join(
        f"{p}={len(por_proc.get(p, []))}" for p in PROCEDENCIAS))

    # A DIVIDA fica visivel toda rodada, e nao enterrada num comentario. Nao
    # reprova o build: e trabalho de bancada, e reprovar aqui so ensinaria a
    # esvaziar a lista sem testar nada.
    a_verificar = sorted(por_proc.get(A_VERIFICAR, []))
    if a_verificar:
        print(f"\n  [!] {len(a_verificar)} campo(s) A_VERIFICAR — razao por LEITURA, sem teste:")
        for c in a_verificar:
            print(f"      {c}")
        print("      Ao exercitar um na bancada, promova para MEDIDO com o que foi observado.")

    if contraditorios:
        print("\n[ERRO] campos DEFINIDOS pelo firmware e listados como herdados:")
        for c in contraditorios:
            print(f"  - {c}")
        print("\nRemova-os de HERDADOS — o codigo manda, a lista so explica o resto.")
        return 1

    # Entrada obsoleta na lista tambem e defeito: ela sugere uma decisao que ja
    # nao existe, e a proxima pessoa confia nela.
    todos = {c for nome, cab, _ in ALVOS for c in campos_da_struct(cab, nome)}
    orfas = sorted(set(HERDADOS) - todos)
    if orfas:
        print("\n[ERRO] a lista de herdados cita campos que nao existem mais:")
        for c in orfas:
            print(f"  - {c}")
        return 1

    invalidas = sorted(c for c, (p, _) in HERDADOS.items() if p not in PROCEDENCIAS)
    if invalidas:
        print("\n[ERRO] procedencia desconhecida em:")
        for c in invalidas:
            print(f"  - {c}")
        return 1

    if sem_decisao:
        print("\n[ERRO] campos SEM DECISAO — o valor vem do que estiver na NVM da placa.")
        print("Numa placa nossa isso funciona por acidente; numa placa zerada, nao.\n")
        for nome, c in sem_decisao:
            print(f"  - {nome}::{c}")
        print("\nOu defina o campo em motor_link.cpp, ou acrescente-o a HERDADOS")
        print("com a procedencia e o motivo. Entrada sem isso nao ajuda ninguem depois.")
        return 1

    print("\ntodo campo tem decisao: definido no firmware ou herdado com procedencia")
    return 0


if __name__ == "__main__":
    sys.exit(main())
