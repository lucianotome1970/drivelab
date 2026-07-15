// ============================================================================
//  DriveLab Firmware
//  main.cpp (handbrake) — Firmware do freio de mão (RP2040): Joystick 1 eixo+botão + protocolo P0 + HX711.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

// DriveLab Firmware — Freio de mão (RP2040) — M5: Joystick (1 eixo + 1 botão) + P0 + load cell (HX711) + flash
// Aparece como "DriveLab Handbrake" (1 eixo Rx 12-bit + 1 botão) E responde o protocolo P0:
//   - telemetria PedalState (0x20)  in   (eixo no slot Clutch; botão no bit0 de Flags — HandbrakeFlags.ButtonPressed)
//   - SettingWrite (0x14) / SettingReadRequest (0x15) / Command (0x02)  out
//   - SettingValue (0x16)  in (resposta de leitura)
// O DriveLab Studio conecta (HidHandbrakeTransport), lê/grava settings e recebe telemetria.
//
// Porta direta de firmware-pedal/src/main.cpp (M4), reduzindo de 3 eixos p/ 1 eixo + botão digital
// com limiar/histerese (espelha DriveLab.Core.Handbrake.HandbrakeDeviceModel.UpdateButton).
//
// ESTADO: escrito SEM placa (não validado em hardware). Suspeitos nº1 na bancada (iguais ao pedal):
//   (a) o plumbing dos OUTPUT reports do TinyUSB (setReportCallback) — ver onSetReport;
//   (b) o report descriptor vendor e o novo layout do Joystick (1 eixo 16-bit + 1 bit de botão + 7 padding).
// Ver checklist de bancada no README.
// Contrato: protocolo P0 (notas internas de projeto)
#include <Arduino.h>
#include <Adafruit_TinyUSB.h>
#include <HX711.h>
#include <EEPROM.h>

// ===================== Constantes do contrato P0 (espelham DriveLab.Core) =====================
static const uint8_t RID_JOYSTICK    = 0x01;
static const uint8_t RID_PEDALSTATE  = 0x20;
static const uint8_t RID_CMD         = 0x02;
static const uint8_t RID_SET_WRITE   = 0x14;
static const uint8_t RID_SET_READREQ = 0x15;
static const uint8_t RID_SET_VALUE   = 0x16;

// SettingType
enum { T_U8 = 0, T_I8 = 1, T_U16 = 2, T_I16 = 3, T_F32 = 4 };
// HandbrakeSettingId (DriveLab.Core.Settings.HandbrakeSettingId)
enum { F_SENSOR = 0, F_INMIN = 1, F_INMAX = 2, F_INVERT = 3, F_SMOOTH = 4,
       F_CP0 = 5, F_CP5 = 10, F_LCSCALE = 11, F_DZLOW = 12, F_DZHIGH = 13,
       F_BTNTHRESH = 14, F_BTNENABLE = 15 };
// PedalCommandId (reaproveitado — CalibrateStart/Stop/Save/LoadDefaults)
enum { CMD_CAL_START = 1, CMD_CAL_STOP = 2, CMD_SAVE = 3, CMD_LOADDEF = 4 };

static const int PAYLOAD = 64;  // ReportConstants.ReportSize

// Botão: histerese de 3 pontos percentuais, igual a HandbrakeDeviceModel.HysteresisPercent.
static const uint8_t BUTTON_HYSTERESIS_PCT = 3;

struct HandbrakeCfg {
  uint8_t  sensorType = 0;                       // 0=Pot,1=Hall,2=LoadCell
  uint16_t inputMin = 0, inputMax = 4095;
  uint8_t  invert = 0, smooth = 0;
  uint8_t  curve[6] = { 0, 20, 40, 60, 80, 100 };
  uint16_t loadCellScale = 1000;
  uint8_t  dzLow = 0, dzHigh = 100;
  uint8_t  buttonThreshold = 70;                 // % do output em que o botão liga
  uint8_t  buttonEnabled = 1;
};

static HandbrakeCfg g_cfg;                       // eixo único (freio de mão)
static uint16_t g_raw = 0;
static uint16_t g_out = 0;
static double   g_smoothed = 0;
static bool     g_cal = false;
static uint16_t g_calMin, g_calMax;
static bool     g_btnPressed = false;            // estado persistente da histerese do botão

static const uint8_t kAdcPin = A0;                // GP26 (pot/hall)

// --- Load cell (HX711), quando sensor_type == 2. Pinos digitais (DT/SCK), iguais ao pedal índice 0. ---
static const uint8_t kHxDT  = 2;  // GP2 (dados)
static const uint8_t kHxSCK = 3;  // GP3 (clock)
static HX711  g_hx;
static long   g_hxLast   = 0;     // última leitura crua (24-bit)
static long   g_hxOffset = 0;     // tara (offset de repouso)

// ===================== HID report descriptor: Joystick (1 eixo + 1 botão) + vendor P0 =====================
static uint8_t const kHidReport[] = {
  // --- Joystick (report 0x01): Rx 16-bit (logical 0..4095) + 1 botão (1 bit + 7 padding) ---
  0x05, 0x01, 0x09, 0x04, 0xA1, 0x01,
    0x85, RID_JOYSTICK,
    // Rx: logical 0..4095, tamanho 16 bits, 1 campo
    0x15, 0x00, 0x26, 0xFF, 0x0F, 0x75, 0x10, 0x95, 0x01, 0x09, 0x33, 0x81, 0x02,
    // Botão 1: usage page Button, 1 bit
    0x05, 0x09, 0x19, 0x01, 0x29, 0x01, 0x15, 0x00, 0x25, 0x01, 0x75, 0x01, 0x95, 0x01, 0x81, 0x02,
    // 7 bits de padding (constante)
    0x75, 0x07, 0x95, 0x01, 0x81, 0x03,
  0xC0,
  // --- Vendor P0 (usage page 0xFF00): reports de 64 bytes, enquadrados por Report ID — IDÊNTICO ao pedal ---
  0x06, 0x00, 0xFF, 0x09, 0x01, 0xA1, 0x01,
    0x15, 0x00, 0x26, 0xFF, 0x00, 0x75, 0x08, 0x95, 0x40,  // logical 0..255, size 8, count 64
    0x85, RID_PEDALSTATE,  0x09, 0x20, 0x81, 0x02,   // Input  0x20 (telemetria)
    0x85, RID_SET_VALUE,   0x09, 0x16, 0x81, 0x02,   // Input  0x16 (resposta de leitura)
    0x85, RID_SET_WRITE,   0x09, 0x14, 0x91, 0x02,   // Output 0x14 (grava setting)
    0x85, RID_SET_READREQ, 0x09, 0x15, 0x91, 0x02,   // Output 0x15 (pede setting)
    0x85, RID_CMD,         0x09, 0x02, 0x91, 0x02,   // Output 0x02 (comando)
  0xC0
};

static Adafruit_USBD_HID g_hid;

// ===================== Pipeline (porta direta do PedalCurve/VirtualPedal) =====================
static double clampd(double v, double lo, double hi) { return v < lo ? lo : (v > hi ? hi : v); }

// Interpola os 6 pontos (0..100 %) nos inputs 0/20/40/60/80/100%.
static double evalCurve(const uint8_t* pts, double x01) {
  double x = clampd(x01, 0.0, 1.0);
  double scaled = x * 5.0;         // 0..5
  int i = (int)scaled;
  if (i >= 5) return pts[5] / 100.0;
  double frac = scaled - i;
  double y = pts[i] + (pts[i + 1] - pts[i]) * frac;
  return y / 100.0;
}

// raw (0..4095) -> output (0..65535), aplicando min/max, invert, deadzone, curva, suavização.
static uint16_t runPipeline(uint16_t raw) {
  const HandbrakeCfg& c = g_cfg;
  double norm = (c.inputMax > c.inputMin)
      ? clampd((double)(raw - c.inputMin) / (c.inputMax - c.inputMin), 0.0, 1.0)
      : 0.0;
  if (c.invert) norm = 1.0 - norm;
  double lo = c.dzLow / 100.0, hi = c.dzHigh / 100.0;
  norm = (hi > lo) ? clampd((norm - lo) / (hi - lo), 0.0, 1.0) : 0.0;
  double outp = evalCurve(c.curve, norm);
  double target = clampd(outp, 0.0, 1.0) * 65535.0;
  double alpha = clampd(c.smooth / 100.0, 0.0, 0.95);
  g_smoothed = g_smoothed * alpha + target * (1.0 - alpha);
  return (uint16_t)(g_smoothed + 0.5);
}

// ===================== Botão: limiar + histerese (espelha HandbrakeDeviceModel.UpdateButton) =====================
static void updateButton(uint16_t outputRaw) {
  if (!g_cfg.buttonEnabled) { g_btnPressed = false; return; }
  double outputPct = outputRaw / 655.35;  // 0..65535 -> 0..100
  if (g_btnPressed)
    g_btnPressed = outputPct >= (double)g_cfg.buttonThreshold - BUTTON_HYSTERESIS_PCT;
  else
    g_btnPressed = outputPct >= (double)g_cfg.buttonThreshold;
}

// ===================== Settings: get/set (eixo único — index do wire é ignorado) =====================
static uint8_t typeForField(uint8_t f) {
  switch (f) {
    case F_INMIN: case F_INMAX: case F_LCSCALE: return T_U16;
    default: return T_U8;  // sensor/invert/smooth/curve/deadzone/buttonThreshold/buttonEnabled
  }
}

static double readField(uint8_t f) {
  const HandbrakeCfg& c = g_cfg;
  switch (f) {
    case F_SENSOR:     return c.sensorType;
    case F_INMIN:      return c.inputMin;
    case F_INMAX:      return c.inputMax;
    case F_INVERT:     return c.invert;
    case F_SMOOTH:     return c.smooth;
    case F_LCSCALE:    return c.loadCellScale;
    case F_DZLOW:      return c.dzLow;
    case F_DZHIGH:     return c.dzHigh;
    case F_BTNTHRESH:  return c.buttonThreshold;
    case F_BTNENABLE:  return c.buttonEnabled;
    default:
      if (f >= F_CP0 && f <= F_CP5) return c.curve[f - F_CP0];
      return 0;
  }
}

static void writeField(uint8_t f, double v) {
  HandbrakeCfg& c = g_cfg;
  switch (f) {
    case F_SENSOR:     c.sensorType = (uint8_t)v; break;
    case F_INMIN:      c.inputMin = (uint16_t)v; break;
    case F_INMAX:      c.inputMax = (uint16_t)v; break;
    case F_INVERT:     c.invert = (uint8_t)v; break;
    case F_SMOOTH:     c.smooth = (uint8_t)v; break;
    case F_LCSCALE:    c.loadCellScale = (uint16_t)v; break;
    case F_DZLOW:      c.dzLow = (uint8_t)v; break;
    case F_DZHIGH:     c.dzHigh = (uint8_t)v; break;
    case F_BTNTHRESH:  c.buttonThreshold = (uint8_t)v; break;
    case F_BTNENABLE:  c.buttonEnabled = (uint8_t)v; break;
    default:
      if (f >= F_CP0 && f <= F_CP5) c.curve[f - F_CP0] = (uint8_t)v;
      break;
  }
}

static double decodeValue(const uint8_t* buf) {  // buf = payload; [2]=type, [3..]=valor LE
  uint8_t type = buf[2];
  const uint8_t* v = buf + 3;
  switch (type) {
    case T_U8:  return v[0];
    case T_I8:  return (int8_t)v[0];
    case T_U16: return (uint16_t)(v[0] | (v[1] << 8));
    case T_I16: return (int16_t)(v[0] | (v[1] << 8));
    case T_F32: { float f; memcpy(&f, v, 4); return f; }
  }
  return 0;
}

// index é sempre ecoado como recebido (eixo único: o app sempre manda 0) — mantém o layout
// do wire compatível com o pedal, mas a leitura/escrita real ignora o índice.
static void sendSettingValue(uint8_t field, uint8_t index) {
  uint8_t out[PAYLOAD]; memset(out, 0, sizeof(out));
  uint8_t type = typeForField(field);
  out[0] = field; out[1] = index; out[2] = type;
  double val = readField(field);
  if (type == T_U16 || type == T_I16) {
    uint16_t u = (uint16_t)val; out[3] = u & 0xFF; out[4] = (u >> 8) & 0xFF;
  } else {
    out[3] = (uint8_t)val;
  }
  g_hid.sendReport(RID_SET_VALUE, out, PAYLOAD);
}

static void seedDefaults() { g_cfg = HandbrakeCfg(); }

// ===================== leitura do sensor (ADC ou HX711) =====================
// Retorna raw 0..4095 independente do tipo de sensor; o pipeline normaliza depois.
static uint16_t readSensorRaw() {
  if (g_cfg.sensorType == 2) {  // LoadCell (HX711)
    if (g_hx.is_ready()) g_hxLast = g_hx.read();  // não bloqueia se não estiver pronto
    long v = g_hxLast - g_hxOffset;
    long sc = g_cfg.loadCellScale < 1 ? 1 : (long)g_cfg.loadCellScale;
    long out = v / sc;
    if (out < 0) out = 0;
    if (out > 4095) out = 4095;
    return (uint16_t)out;
  }
  return (uint16_t)analogRead(kAdcPin);  // Pot/Hall
}

// ===================== persistência em flash (EEPROM emulada) =====================
static const uint32_t FLASH_MAGIC = 0x444C4831;  // "DLH1" — freio de mão (distinto do pedal "DLP1")

static void saveToFlash() {
  int addr = 0;
  EEPROM.put(addr, FLASH_MAGIC); addr += sizeof(FLASH_MAGIC);
  EEPROM.put(addr, g_cfg); addr += sizeof(HandbrakeCfg);  // já inclui buttonThreshold/buttonEnabled
  EEPROM.commit();
}

// Carrega da flash se houver config válida. Retorna false se vazio (usar defaults).
static bool loadFromFlash() {
  int addr = 0;
  uint32_t magic; EEPROM.get(addr, magic); addr += sizeof(magic);
  if (magic != FLASH_MAGIC) return false;
  EEPROM.get(addr, g_cfg); addr += sizeof(HandbrakeCfg);
  return true;
}

// ===================== OUTPUT reports (host -> device) =====================
// ATENÇÃO (bancada): confirmar como o Adafruit_TinyUSB entrega OUTPUT reports.
// Aqui assumimos: report_id = ID do report; buffer = payload (sem o ID). Se vier diferente
// (ex.: ID no buffer[0]), ajustar — igual ao risco já documentado no firmware-pedal.
static void onSetReport(uint8_t report_id, hid_report_type_t /*type*/, uint8_t const* buf, uint16_t len) {
  if (len < 2) return;
  switch (report_id) {
    case RID_SET_WRITE: {                 // [0]=field [1]=index [2]=type [3..]=valor
      uint8_t field = buf[0];
      // index é ignorado: eixo único (sempre 0).
      writeField(field, decodeValue(buf));
      break;
    }
    case RID_SET_READREQ: {               // [0]=field [1]=index
      uint8_t field = buf[0], index = buf[1];
      sendSettingValue(field, index);
      break;
    }
    case RID_CMD: {                       // [0]=cmd [1]=arg
      uint8_t cmd = buf[0];
      if (cmd == CMD_LOADDEF) { seedDefaults(); }
      else if (cmd == CMD_CAL_START) { g_cal = true; g_calMin = 0xFFFF; g_calMax = 0; }
      else if (cmd == CMD_CAL_STOP && g_cal) {
        g_cal = false;
        if (g_calMax >= g_calMin) { g_cfg.inputMin = g_calMin; g_cfg.inputMax = g_calMax; }
      }
      else if (cmd == CMD_SAVE) { saveToFlash(); }  // persiste a config atual (eixo + botão)
      break;
    }
  }
}

// ===================== Setup / Loop =====================
void setup() {
  Serial.begin(115200);
  analogReadResolution(12);

  // Carrega a config salva na flash; se vazia, usa defaults.
  EEPROM.begin(256);
  if (!loadFromFlash()) seedDefaults();

  // Inicializa o HX711 (pinos); a leitura só ocorre se sensor_type==LoadCell.
  g_hx.begin(kHxDT, kHxSCK);
  if (g_cfg.sensorType == 2 && g_hx.is_ready()) g_hxOffset = g_hx.read();  // tara

  g_hid.setReportDescriptor(kHidReport, sizeof(kHidReport));
  g_hid.setReportCallback(nullptr, onSetReport);  // (get_cb, set_cb)
  g_hid.begin();

  const unsigned long t0 = millis();
  while (!TinyUSBDevice.mounted() && (millis() - t0) < 3000) delay(10);
  Serial.println("=== DriveLab Freio de mão — M5 (Joystick 1 eixo+botão + P0 + HX711 + flash) ===");
}

static unsigned long g_lastTelem = 0;

void loop() {
  if (!g_hid.ready()) { delay(1); return; }

  // 1) ler sensor (ADC pot/hall OU HX711 load cell, por sensor_type)
  g_raw = readSensorRaw();                  // 0..4095
  if (g_cal) {
    if (g_raw < g_calMin) g_calMin = g_raw;
    if (g_raw > g_calMax) g_calMax = g_raw;
  }
  g_out = runPipeline(g_raw);
  updateButton(g_out);

  // 2) Joystick: eixo Rx (16-bit, valor 12-bit reescalado) + botão (bit0) + 7 bits padding
  uint8_t joy[3];
  uint16_t axis12 = (uint16_t)(g_out >> 4);
  joy[0] = axis12 & 0xFF;
  joy[1] = (axis12 >> 8) & 0xFF;
  joy[2] = g_btnPressed ? 0x01 : 0x00;
  g_hid.sendReport(RID_JOYSTICK, joy, sizeof(joy));

  // 3) telemetria PedalState (0x20) a ~100 Hz — eixo no slot Clutch (offset 5..8), demais zerados,
  //    Flags (offset 4) bit0 = HandbrakeFlags.ButtonPressed
  if (millis() - g_lastTelem >= 10) {
    g_lastTelem = millis();
    uint8_t st[PAYLOAD]; memset(st, 0, sizeof(st));
    // [0..3] firmware version (0.1.0.0 placeholder), [4] flags
    st[1] = 1;
    st[4] = g_btnPressed ? 0x01 : 0x00;
    // [5..8] = Clutch (eixo do freio de mão): raw(u16 LE), output(u16 LE)
    int off = 5;
    st[off++] = g_raw & 0xFF; st[off++] = (g_raw >> 8) & 0xFF;
    st[off++] = g_out & 0xFF; st[off++] = (g_out >> 8) & 0xFF;
    // [9..16] = Brake/Throttle: zerados (freio de mão não os usa)
    g_hid.sendReport(RID_PEDALSTATE, st, PAYLOAD);
  }

  delay(2);
}
