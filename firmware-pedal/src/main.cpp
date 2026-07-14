// DriveLab Firmware — Pedaleira (RP2040) — M2: HID Joystick + canal vendor P0
// Aparece como "DriveLab Pedal" (3 eixos 12-bit) E responde o protocolo P0:
//   - telemetria PedalState (0x20)  in
//   - SettingWrite (0x14) / SettingReadRequest (0x15) / Command (0x02)  out
//   - SettingValue (0x16)  in (resposta de leitura)
// O DriveLab Studio conecta (HidPedalTransport), lê/grava settings e recebe telemetria.
//
// ESTADO: escrito SEM placa (não validado em hardware). Suspeitos nº1 na bancada:
//   (a) o plumbing dos OUTPUT reports do TinyUSB (setReportCallback) — ver onSetReport;
//   (b) o report descriptor vendor. Ver README (seção M2).
// Ainda: settings em RAM (flash = M4); load cell/HX711 = M3 (aqui só ADC analógico).
// Contrato: docs/superpowers/specs/2026-07-13-drivelab-pedals-p0-design.md
#include <Arduino.h>
#include <Adafruit_TinyUSB.h>

// ===================== Constantes do contrato P0 (espelham DriveLab.Core) =====================
static const uint8_t RID_JOYSTICK    = 0x01;
static const uint8_t RID_PEDALSTATE  = 0x20;
static const uint8_t RID_CMD         = 0x02;
static const uint8_t RID_SET_WRITE   = 0x14;
static const uint8_t RID_SET_READREQ = 0x15;
static const uint8_t RID_SET_VALUE   = 0x16;

// SettingType
enum { T_U8 = 0, T_I8 = 1, T_U16 = 2, T_I16 = 3, T_F32 = 4 };
// PedalSettingId
enum { F_SENSOR = 0, F_INMIN = 1, F_INMAX = 2, F_INVERT = 3, F_SMOOTH = 4,
       F_CP0 = 5, F_CP5 = 10, F_LCSCALE = 11, F_DZLOW = 12, F_DZHIGH = 13 };
// PedalCommandId
enum { CMD_CAL_START = 1, CMD_CAL_STOP = 2, CMD_SAVE = 3, CMD_LOADDEF = 4 };

static const int PAYLOAD = 64;  // ReportConstants.ReportSize

struct PedalCfg {
  uint8_t  sensorType = 0;                       // 0=Pot,1=Hall,2=LoadCell (M3)
  uint16_t inputMin = 0, inputMax = 4095;
  uint8_t  invert = 0, smooth = 0;
  uint8_t  curve[6] = { 0, 20, 40, 60, 80, 100 };
  uint16_t loadCellScale = 1000;
  uint8_t  dzLow = 0, dzHigh = 100;
};

static PedalCfg g_cfg[3];                       // 0=embreagem,1=freio,2=acelerador
static uint16_t g_raw[3] = { 0, 0, 0 };
static uint16_t g_out[3] = { 0, 0, 0 };
static double   g_smoothed[3] = { 0, 0, 0 };
static bool     g_cal[3] = { false, false, false };
static uint16_t g_calMin[3], g_calMax[3];

static const uint8_t kAdcPin[3] = { A0, A1, A2 };  // GP26/GP27/GP28

// ===================== HID report descriptor: Joystick + vendor P0 =====================
static uint8_t const kHidReport[] = {
  // --- Joystick (report 0x01): Rx/Ry/Rz 12-bit (0..4095) ---
  0x05, 0x01, 0x09, 0x04, 0xA1, 0x01,
    0x85, RID_JOYSTICK, 0x15, 0x00, 0x26, 0xFF, 0x0F, 0x75, 0x10, 0x95, 0x03,
    0x09, 0x33, 0x09, 0x34, 0x09, 0x35, 0x81, 0x02,
  0xC0,
  // --- Vendor P0 (usage page 0xFF00): reports de 64 bytes, enquadrados por Report ID ---
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

// ===================== Pipeline (porta direta do PedalCurve) =====================
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
static uint16_t runPipeline(int p, uint16_t raw) {
  const PedalCfg& c = g_cfg[p];
  double norm = (c.inputMax > c.inputMin)
      ? clampd((double)(raw - c.inputMin) / (c.inputMax - c.inputMin), 0.0, 1.0)
      : 0.0;
  if (c.invert) norm = 1.0 - norm;
  double lo = c.dzLow / 100.0, hi = c.dzHigh / 100.0;
  norm = (hi > lo) ? clampd((norm - lo) / (hi - lo), 0.0, 1.0) : 0.0;
  double outp = evalCurve(c.curve, norm);
  double target = clampd(outp, 0.0, 1.0) * 65535.0;
  double alpha = clampd(c.smooth / 100.0, 0.0, 0.95);
  g_smoothed[p] = g_smoothed[p] * alpha + target * (1.0 - alpha);
  return (uint16_t)(g_smoothed[p] + 0.5);
}

// ===================== Settings: get/set por (campo, pedal) =====================
static uint8_t typeForField(uint8_t f) {
  switch (f) {
    case F_INMIN: case F_INMAX: case F_LCSCALE: return T_U16;
    default: return T_U8;  // sensor/invert/smooth/curve/deadzone
  }
}

static double readField(int p, uint8_t f) {
  const PedalCfg& c = g_cfg[p];
  switch (f) {
    case F_SENSOR:  return c.sensorType;
    case F_INMIN:   return c.inputMin;
    case F_INMAX:   return c.inputMax;
    case F_INVERT:  return c.invert;
    case F_SMOOTH:  return c.smooth;
    case F_LCSCALE: return c.loadCellScale;
    case F_DZLOW:   return c.dzLow;
    case F_DZHIGH:  return c.dzHigh;
    default:
      if (f >= F_CP0 && f <= F_CP5) return c.curve[f - F_CP0];
      return 0;
  }
}

static void writeField(int p, uint8_t f, double v) {
  PedalCfg& c = g_cfg[p];
  switch (f) {
    case F_SENSOR:  c.sensorType = (uint8_t)v; break;
    case F_INMIN:   c.inputMin = (uint16_t)v; break;
    case F_INMAX:   c.inputMax = (uint16_t)v; break;
    case F_INVERT:  c.invert = (uint8_t)v; break;
    case F_SMOOTH:  c.smooth = (uint8_t)v; break;
    case F_LCSCALE: c.loadCellScale = (uint16_t)v; break;
    case F_DZLOW:   c.dzLow = (uint8_t)v; break;
    case F_DZHIGH:  c.dzHigh = (uint8_t)v; break;
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

static void sendSettingValue(uint8_t field, uint8_t index) {
  uint8_t out[PAYLOAD]; memset(out, 0, sizeof(out));
  uint8_t type = typeForField(field);
  out[0] = field; out[1] = index; out[2] = type;
  double val = readField(index, field);
  if (type == T_U16 || type == T_I16) {
    uint16_t u = (uint16_t)val; out[3] = u & 0xFF; out[4] = (u >> 8) & 0xFF;
  } else {
    out[3] = (uint8_t)val;
  }
  g_hid.sendReport(RID_SET_VALUE, out, PAYLOAD);
}

static void seedDefaults() { for (int i = 0; i < 3; i++) g_cfg[i] = PedalCfg(); }

// ===================== OUTPUT reports (host -> device) =====================
// ATENÇÃO (bancada): confirmar como o Adafruit_TinyUSB entrega OUTPUT reports.
// Aqui assumimos: report_id = ID do report; buffer = payload (sem o ID). Se vier diferente
// (ex.: ID no buffer[0]), ajustar. É o principal risco deste M2 sem hardware.
static void onSetReport(uint8_t report_id, hid_report_type_t /*type*/, uint8_t const* buf, uint16_t len) {
  if (len < 2) return;
  switch (report_id) {
    case RID_SET_WRITE: {                 // [0]=field [1]=index [2]=type [3..]=valor
      uint8_t field = buf[0], index = buf[1];
      if (index < 3) writeField(index, field, decodeValue(buf));
      break;
    }
    case RID_SET_READREQ: {               // [0]=field [1]=index
      uint8_t field = buf[0], index = buf[1];
      if (index < 3) sendSettingValue(field, index);
      break;
    }
    case RID_CMD: {                       // [0]=cmd [1]=arg
      uint8_t cmd = buf[0], arg = buf[1];
      if (cmd == CMD_LOADDEF) { seedDefaults(); }
      else if (cmd == CMD_CAL_START && arg < 3) { g_cal[arg] = true; g_calMin[arg] = 0xFFFF; g_calMax[arg] = 0; }
      else if (cmd == CMD_CAL_STOP && arg < 3 && g_cal[arg]) {
        g_cal[arg] = false;
        if (g_calMax[arg] >= g_calMin[arg]) { g_cfg[arg].inputMin = g_calMin[arg]; g_cfg[arg].inputMax = g_calMax[arg]; }
      }
      // CMD_SAVE = no-op até o M4 (flash).
      break;
    }
  }
}

// ===================== Setup / Loop =====================
void setup() {
  Serial.begin(115200);
  analogReadResolution(12);
  seedDefaults();

  g_hid.setReportDescriptor(kHidReport, sizeof(kHidReport));
  g_hid.setReportCallback(nullptr, onSetReport);  // (get_cb, set_cb)
  g_hid.begin();

  const unsigned long t0 = millis();
  while (!TinyUSBDevice.mounted() && (millis() - t0) < 3000) delay(10);
  Serial.println("=== DriveLab Pedaleira — M2 (HID Joystick + vendor P0) ===");
}

static unsigned long g_lastTelem = 0;

void loop() {
  if (!g_hid.ready()) { delay(1); return; }

  // 1) ler sensores (M2: só ADC analógico; HX711/load cell no M3)
  for (int p = 0; p < 3; p++) {
    g_raw[p] = analogRead(kAdcPin[p]);            // 0..4095
    if (g_cal[p]) {
      if (g_raw[p] < g_calMin[p]) g_calMin[p] = g_raw[p];
      if (g_raw[p] > g_calMax[p]) g_calMax[p] = g_raw[p];
    }
    g_out[p] = runPipeline(p, g_raw[p]);
  }

  // 2) eixos do Joystick (12-bit): usa o output do pipeline reescalado p/ 0..4095
  uint16_t axes[3] = { (uint16_t)(g_out[0] >> 4), (uint16_t)(g_out[1] >> 4), (uint16_t)(g_out[2] >> 4) };
  g_hid.sendReport(RID_JOYSTICK, axes, sizeof(axes));

  // 3) telemetria PedalState (0x20) a ~100 Hz
  if (millis() - g_lastTelem >= 10) {
    g_lastTelem = millis();
    uint8_t st[PAYLOAD]; memset(st, 0, sizeof(st));
    // [0..3] firmware version (0.1.0.0 placeholder), [4] flags=0
    st[1] = 1;
    // [5..16] = por pedal: raw(u16 LE), output(u16 LE)
    int off = 5;
    for (int p = 0; p < 3; p++) {
      st[off++] = g_raw[p] & 0xFF; st[off++] = (g_raw[p] >> 8) & 0xFF;
      st[off++] = g_out[p] & 0xFF; st[off++] = (g_out[p] >> 8) & 0xFF;
    }
    g_hid.sendReport(RID_PEDALSTATE, st, PAYLOAD);
  }

  delay(2);
}
