// ============================================================================
//  DriveLab Firmware
//  main.cpp (wheel/rim) — Firmware do volante removível (RP2040): Gamepad 32 botões + 2 eixos + P0 + WS2812 + flash.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

// DriveLab Firmware — Rim (RP2040) — M4: Gamepad HID + P0 + LEDs WS2812 + flash
// Aparece como "DriveLab Wheel" (32 botões + 2 eixos) E responde o protocolo P0:
//   - telemetria WheelState (0x21)  in
//   - WheelLed (0x18) / SettingWrite (0x14) / SettingReadRequest (0x15) / Command (0x02)  out
//   - SettingValue (0x16)  in (resposta de leitura)
//
// ESTADO: escrito SEM placa (não validado). Suspeitos nº1 (iguais pedal/handbrake):
//   (a) plumbing dos OUTPUT reports do TinyUSB (onSetReport) — WheelLed/SettingWrite;
//   (b) report descriptor (gamepad + vendor). Ver README.
// Contrato: protocolo P0 (notas internas de projeto)
#include <Arduino.h>
#include <Adafruit_TinyUSB.h>
#include <Adafruit_NeoPixel.h>
#include <EEPROM.h>

// ===================== Constantes do contrato P0 (espelham DriveLab.Core) =====================
static const uint8_t RID_JOYSTICK  = 0x01;
static const uint8_t RID_STATE     = 0x21;  // WheelReportIds.State
static const uint8_t RID_LED       = 0x18;  // WheelReportIds.Led
static const uint8_t RID_CMD       = 0x02;
static const uint8_t RID_SET_WRITE = 0x14;
static const uint8_t RID_SET_READREQ = 0x15;
static const uint8_t RID_SET_VALUE = 0x16;

// SettingType (compartilhado)
enum { T_U8 = 0, T_I8 = 1, T_U16 = 2, T_I16 = 3, T_F32 = 4 };
// WheelSettingId
enum { S_CL_MIN = 0, S_CL_MAX = 1, S_CR_MIN = 2, S_CR_MAX = 3,
       S_CL_INV = 4, S_CR_INV = 5, S_MODE = 6, S_BITE = 7,
       S_LED_BRIGHT = 8, S_LED_COUNT = 9 };
// WheelCommandId
enum { CMD_CAL_START = 1, CMD_CAL_STOP = 2, CMD_SAVE = 3, CMD_LOADDEF = 4 };

static const int PAYLOAD = 63;  // ReportConstants.ReportSize (63 = cabe no EP HID de 64 c/ report id)

// ===================== Pinos (ajustáveis) =====================
static const uint8_t kRowPins[3] = { 2, 3, 4 };       // linhas da matriz (saídas)
static const uint8_t kColPins[4] = { 5, 6, 7, 8 };    // colunas da matriz (entradas pull-up)
static const uint8_t kShiftUpPin = 9, kShiftDownPin = 10;
static const uint8_t kClutchPin[2] = { A0, A1 };      // GP26/GP27 (esq./dir.)
static const uint8_t kEncPinA[2] = { 11, 13 };
static const uint8_t kEncPinB[2] = { 12, 14 };
static const uint8_t kEncPushPin[2] = { 15, 28 };     // GP28 = A2 usado como digital
static const uint8_t kLedDataPin = 16;                // WS2812 (NeoPixel onboard do Zero)

// Layout dos bits de botão (bitmap de 32):
//   0..11  = matriz 3x4 (linha*4 + coluna)
//   12,13  = shift up / shift down
//   14,15  = enc0 CW / CCW (pulso momentâneo)
//   16,17  = enc1 CW / CCW (pulso momentâneo)
//   18,19  = enc0 push / enc1 push
static const int BIT_SHIFT_UP = 12, BIT_SHIFT_DOWN = 13;
static const int BIT_ENC_CW[2] = { 14, 16 };
static const int BIT_ENC_CCW[2] = { 15, 17 };
static const int BIT_ENC_PUSH[2] = { 18, 19 };

// ===================== Config persistente =====================
struct WheelCfg {
  uint16_t clutchMin[2] = { 0, 0 };
  uint16_t clutchMax[2] = { 4095, 4095 };
  uint8_t  clutchInvert[2] = { 0, 0 };
  uint8_t  clutchMode = 0;        // 0 = combinado, 1 = independente
  uint8_t  clutchBitePoint = 50;  // 0..100
  uint8_t  ledBrightness = 128;
  uint8_t  ledCount = 8;
};

static WheelCfg g_cfg;
static uint32_t g_buttons = 0;       // bitmap atual (matriz + shift + encoder pushes + pulsos)
static uint16_t g_clutchRaw[2] = { 0, 0 };
static uint16_t g_clutchOut[2] = { 0, 0 };
static int8_t   g_encDelta[4] = { 0, 0, 0, 0 };  // acumulado desde a última telemetria
static uint8_t  g_encPrev[2] = { 0, 0 };         // último estado A/B por encoder
static bool     g_cal[2] = { false, false };
static uint16_t g_calMin[2], g_calMax[2];

static Adafruit_USBD_HID g_hid;
static Adafruit_NeoPixel g_pixels(WheelCfg().ledCount, kLedDataPin, NEO_GRB + NEO_KHZ800);

// ===================== HID report descriptor: Gamepad (32 botões + 2 eixos) + vendor P0 =====================
static uint8_t const kHidReport[] = {
  // --- Gamepad (report 0x01): 32 botões + X/Y 16-bit (logical 0..4095) ---
  0x05, 0x01, 0x09, 0x05, 0xA1, 0x01,
    0x85, RID_JOYSTICK,
    // 32 botões (1 bit cada)
    0x05, 0x09, 0x19, 0x01, 0x29, 0x20, 0x15, 0x00, 0x25, 0x01, 0x75, 0x01, 0x95, 0x20, 0x81, 0x02,
    // X, Y: logical 0..4095, 16 bits, 2 campos
    0x05, 0x01, 0x09, 0x30, 0x09, 0x31, 0x15, 0x00, 0x26, 0xFF, 0x0F, 0x75, 0x10, 0x95, 0x02, 0x81, 0x02,
  0xC0,
  // --- Vendor P0 (usage page 0xFF00): reports de 64 bytes ---
  0x06, 0x00, 0xFF, 0x09, 0x01, 0xA1, 0x01,
    0x15, 0x00, 0x26, 0xFF, 0x00, 0x75, 0x08, 0x95, 0x3F,  // logical 0..255, size 8, count 63 (+id=64, cabe no EP)
    0x85, RID_STATE,       0x09, 0x21, 0x81, 0x02,   // Input  0x21 (telemetria)
    0x85, RID_SET_VALUE,   0x09, 0x16, 0x81, 0x02,   // Input  0x16 (resposta de leitura)
    0x85, RID_LED,         0x09, 0x18, 0x91, 0x02,   // Output 0x18 (cores RGB)
    0x85, RID_SET_WRITE,   0x09, 0x14, 0x91, 0x02,   // Output 0x14 (grava setting)
    0x85, RID_SET_READREQ, 0x09, 0x15, 0x91, 0x02,   // Output 0x15 (pede setting)
    0x85, RID_CMD,         0x09, 0x02, 0x91, 0x02,   // Output 0x02 (comando)
  0xC0
};

// ===================== helpers =====================
static double clampd(double v, double lo, double hi) { return v < lo ? lo : (v > hi ? hi : v); }
static void setBit(uint32_t& mask, int bit, bool on) { if (on) mask |= (1u << bit); else mask &= ~(1u << bit); }

// raw (0..4095) -> out (0..4095) para uma pá: min/max, invert, bite point (abaixo do bite = 0).
static uint16_t clutchPipeline(int i, uint16_t raw) {
  double norm = (g_cfg.clutchMax[i] > g_cfg.clutchMin[i])
      ? clampd((double)(raw - g_cfg.clutchMin[i]) / (g_cfg.clutchMax[i] - g_cfg.clutchMin[i]), 0.0, 1.0)
      : 0.0;
  if (g_cfg.clutchInvert[i]) norm = 1.0 - norm;
  double bite = g_cfg.clutchBitePoint / 100.0;
  norm = (bite < 1.0) ? clampd((norm - bite) / (1.0 - bite), 0.0, 1.0) : 0.0;  // acima do bite mapeia 0..1
  return (uint16_t)(norm * 4095.0 + 0.5);
}

// ===================== matriz de botões =====================
static void scanButtons() {
  // limpa só os bits da matriz (0..11) e shift; encoders são tratados à parte.
  for (int b = 0; b <= 11; b++) setBit(g_buttons, b, false);
  for (int r = 0; r < 3; r++) {
    digitalWrite(kRowPins[r], LOW);       // ativa a linha
    delayMicroseconds(5);
    for (int c = 0; c < 4; c++) {
      bool pressed = (digitalRead(kColPins[c]) == LOW);  // colunas em pull-up
      setBit(g_buttons, r * 4 + c, pressed);
    }
    digitalWrite(kRowPins[r], HIGH);      // desativa a linha
  }
  setBit(g_buttons, BIT_SHIFT_UP,   digitalRead(kShiftUpPin) == LOW);
  setBit(g_buttons, BIT_SHIFT_DOWN, digitalRead(kShiftDownPin) == LOW);
  setBit(g_buttons, BIT_ENC_PUSH[0], digitalRead(kEncPushPin[0]) == LOW);
  setBit(g_buttons, BIT_ENC_PUSH[1], digitalRead(kEncPushPin[1]) == LOW);
}

// ===================== encoders (quadratura) =====================
static const int8_t kQuadTable[16] = { 0,-1,1,0, 1,0,0,-1, -1,0,0,1, 0,1,-1,0 };
static void scanEncoders() {
  for (int e = 0; e < 2; e++) {
    uint8_t a = digitalRead(kEncPinA[e]);
    uint8_t b = digitalRead(kEncPinB[e]);
    uint8_t cur = (a << 1) | b;
    uint8_t idx = (g_encPrev[e] << 2) | cur;
    int8_t step = kQuadTable[idx & 0x0F];
    if (step != 0) {
      int v = g_encDelta[e] + step;      // acumula p/ telemetria (satura em ±127)
      g_encDelta[e] = (int8_t)(v > 127 ? 127 : (v < -128 ? -128 : v));
      // pulso momentâneo de botão (limpo no próximo sendReport do gamepad)
      setBit(g_buttons, step > 0 ? BIT_ENC_CW[e] : BIT_ENC_CCW[e], true);
    }
    g_encPrev[e] = cur;
  }
}

// ===================== Settings get/set =====================
static uint8_t typeForField(uint8_t f) {
  switch (f) {
    case S_CL_MIN: case S_CL_MAX: case S_CR_MIN: case S_CR_MAX: return T_U16;
    default: return T_U8;
  }
}
static double readField(uint8_t f) {
  switch (f) {
    case S_CL_MIN: return g_cfg.clutchMin[0];
    case S_CL_MAX: return g_cfg.clutchMax[0];
    case S_CR_MIN: return g_cfg.clutchMin[1];
    case S_CR_MAX: return g_cfg.clutchMax[1];
    case S_CL_INV: return g_cfg.clutchInvert[0];
    case S_CR_INV: return g_cfg.clutchInvert[1];
    case S_MODE:   return g_cfg.clutchMode;
    case S_BITE:   return g_cfg.clutchBitePoint;
    case S_LED_BRIGHT: return g_cfg.ledBrightness;
    case S_LED_COUNT:  return g_cfg.ledCount;
    default: return 0;
  }
}
static void writeField(uint8_t f, double v) {
  switch (f) {
    case S_CL_MIN: g_cfg.clutchMin[0] = (uint16_t)v; break;
    case S_CL_MAX: g_cfg.clutchMax[0] = (uint16_t)v; break;
    case S_CR_MIN: g_cfg.clutchMin[1] = (uint16_t)v; break;
    case S_CR_MAX: g_cfg.clutchMax[1] = (uint16_t)v; break;
    case S_CL_INV: g_cfg.clutchInvert[0] = (uint8_t)v; break;
    case S_CR_INV: g_cfg.clutchInvert[1] = (uint8_t)v; break;
    case S_MODE:   g_cfg.clutchMode = (uint8_t)v; break;
    case S_BITE:   g_cfg.clutchBitePoint = (uint8_t)v; break;
    case S_LED_BRIGHT: g_cfg.ledBrightness = (uint8_t)v; g_pixels.setBrightness(g_cfg.ledBrightness); break;
    case S_LED_COUNT:  g_cfg.ledCount = (uint8_t)v; g_pixels.updateLength(g_cfg.ledCount); break;
  }
}
static double decodeValue(const uint8_t* buf) {  // buf = payload; [2]=type, [3..]=valor LE
  const uint8_t* v = buf + 3;
  switch (buf[2]) {
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
  double val = readField(field);
  if (type == T_U16 || type == T_I16) {
    uint16_t u = (uint16_t)val; out[3] = u & 0xFF; out[4] = (u >> 8) & 0xFF;
  } else {
    out[3] = (uint8_t)val;
  }
  g_hid.sendReport(RID_SET_VALUE, out, PAYLOAD);
}

// ===================== LEDs (WS2812) =====================
static void applyLedReport(const uint8_t* buf, uint16_t len) {
  uint8_t count = buf[0];
  uint8_t brightness = buf[1];
  if (count > 20) count = 20;
  g_pixels.setBrightness(brightness);
  for (uint8_t i = 0; i < count && i < g_pixels.numPixels(); i++) {
    uint16_t o = 2 + i * 3;
    if (o + 2 >= len) break;
    g_pixels.setPixelColor(i, g_pixels.Color(buf[o], buf[o + 1], buf[o + 2]));
  }
  g_pixels.show();
}

// ===================== persistência em flash =====================
static const uint32_t FLASH_MAGIC = 0x444C5731;  // "DLW1" — rim (distinto de pedal "DLP1"/handbrake "DLH1")
static void saveToFlash() {
  int addr = 0;
  EEPROM.put(addr, FLASH_MAGIC); addr += sizeof(FLASH_MAGIC);
  EEPROM.put(addr, g_cfg); addr += sizeof(WheelCfg);
  EEPROM.commit();
}
static bool loadFromFlash() {
  int addr = 0;
  uint32_t magic; EEPROM.get(addr, magic); addr += sizeof(magic);
  if (magic != FLASH_MAGIC) return false;
  EEPROM.get(addr, g_cfg); addr += sizeof(WheelCfg);
  return true;
}
static void seedDefaults() { g_cfg = WheelCfg(); }

// ===================== OUTPUT reports (host -> device) =====================
// ATENÇÃO (bancada): confirmar como o Adafruit_TinyUSB entrega OUTPUT reports.
// Assumimos: report_id = ID do report; buf = payload (sem o ID). Igual ao risco do pedal.
static void onSetReport(uint8_t report_id, hid_report_type_t /*type*/, uint8_t const* buf, uint16_t len) {
  switch (report_id) {
    case RID_LED:
      applyLedReport(buf, len);
      break;
    case RID_SET_WRITE:                    // [0]=field [1]=index [2]=type [3..]=valor
      if (len >= 3) writeField(buf[0], decodeValue(buf));
      break;
    case RID_SET_READREQ:                  // [0]=field [1]=index
      if (len >= 2) sendSettingValue(buf[0], buf[1]);
      break;
    case RID_CMD: {                        // [0]=cmd [1]=arg
      if (len < 1) break;
      uint8_t cmd = buf[0];
      if (cmd == CMD_LOADDEF) seedDefaults();
      else if (cmd == CMD_CAL_START) { for (int i = 0; i < 2; i++) { g_cal[i] = true; g_calMin[i] = 0xFFFF; g_calMax[i] = 0; } }
      else if (cmd == CMD_CAL_STOP) {
        for (int i = 0; i < 2; i++) {
          if (g_cal[i]) { g_cal[i] = false; if (g_calMax[i] >= g_calMin[i]) { g_cfg.clutchMin[i] = g_calMin[i]; g_cfg.clutchMax[i] = g_calMax[i]; } }
        }
      }
      else if (cmd == CMD_SAVE) saveToFlash();
      break;
    }
  }
}

// ===================== Setup / Loop =====================
void setup() {
  Serial.begin(115200);
  analogReadResolution(12);

  for (int r = 0; r < 3; r++) { pinMode(kRowPins[r], OUTPUT); digitalWrite(kRowPins[r], HIGH); }
  for (int c = 0; c < 4; c++) pinMode(kColPins[c], INPUT_PULLUP);
  pinMode(kShiftUpPin, INPUT_PULLUP);
  pinMode(kShiftDownPin, INPUT_PULLUP);
  for (int e = 0; e < 2; e++) {
    pinMode(kEncPinA[e], INPUT_PULLUP);
    pinMode(kEncPinB[e], INPUT_PULLUP);
    pinMode(kEncPushPin[e], INPUT_PULLUP);
    g_encPrev[e] = (digitalRead(kEncPinA[e]) << 1) | digitalRead(kEncPinB[e]);
  }

  EEPROM.begin(256);
  if (!loadFromFlash()) seedDefaults();

  g_pixels.updateLength(g_cfg.ledCount);
  g_pixels.begin();
  g_pixels.setBrightness(g_cfg.ledBrightness);
  g_pixels.clear();
  g_pixels.show();

  g_hid.setReportDescriptor(kHidReport, sizeof(kHidReport));
  g_hid.setReportCallback(nullptr, onSetReport);  // (get_cb, set_cb)
  g_hid.begin();

  const unsigned long t0 = millis();
  while (!TinyUSBDevice.mounted() && (millis() - t0) < 3000) delay(10);
  Serial.println("=== DriveLab Volante (rim) — M4 (Gamepad 32 botoes + 2 eixos + P0 + WS2812 + flash) ===");
}

static unsigned long g_lastTelem = 0;

void loop() {
  if (!g_hid.ready()) { delay(1); return; }

  // 1) entradas
  scanEncoders();   // antes da matriz: pode setar pulsos de botão
  scanButtons();

  // 2) pás de embreagem (ADC -> pipeline)
  for (int i = 0; i < 2; i++) {
    g_clutchRaw[i] = (uint16_t)analogRead(kClutchPin[i]);
    if (g_cal[i]) {
      if (g_clutchRaw[i] < g_calMin[i]) g_calMin[i] = g_clutchRaw[i];
      if (g_clutchRaw[i] > g_calMax[i]) g_calMax[i] = g_clutchRaw[i];
    }
    g_clutchOut[i] = clutchPipeline(i, g_clutchRaw[i]);
  }
  // modo combinado: eixo X = max das duas pás; Y espelha. Independente: X=esq, Y=dir.
  uint16_t axisX = g_cfg.clutchMode == 0 ? max(g_clutchOut[0], g_clutchOut[1]) : g_clutchOut[0];
  uint16_t axisY = g_cfg.clutchMode == 0 ? axisX : g_clutchOut[1];

  // 3) Gamepad: 4 bytes de botões (LE) + X + Y (16-bit)
  uint8_t joy[8];
  joy[0] = g_buttons & 0xFF; joy[1] = (g_buttons >> 8) & 0xFF;
  joy[2] = (g_buttons >> 16) & 0xFF; joy[3] = (g_buttons >> 24) & 0xFF;
  joy[4] = axisX & 0xFF; joy[5] = (axisX >> 8) & 0xFF;
  joy[6] = axisY & 0xFF; joy[7] = (axisY >> 8) & 0xFF;
  g_hid.sendReport(RID_JOYSTICK, joy, sizeof(joy));
  // limpa os pulsos momentâneos de encoder (CW/CCW) após enviá-los uma vez
  for (int e = 0; e < 2; e++) { setBit(g_buttons, BIT_ENC_CW[e], false); setBit(g_buttons, BIT_ENC_CCW[e], false); }

  // 4) telemetria WheelState (0x21) a ~100 Hz
  if (millis() - g_lastTelem >= 10) {
    g_lastTelem = millis();
    uint8_t st[PAYLOAD]; memset(st, 0, sizeof(st));
    // [0..3] fw 0.1.0.0
    st[1] = 1;
    // [4] flags (bit0 Calibrated se ambas as pás calibradas — placeholder simples: 0)
    // [5..8] buttons u32 LE
    st[5] = g_buttons & 0xFF; st[6] = (g_buttons >> 8) & 0xFF;
    st[7] = (g_buttons >> 16) & 0xFF; st[8] = (g_buttons >> 24) & 0xFF;
    // [9..12] clutch esq (raw, out); [13..16] clutch dir (raw, out)
    st[9]  = g_clutchRaw[0] & 0xFF; st[10] = (g_clutchRaw[0] >> 8) & 0xFF;
    st[11] = g_clutchOut[0] & 0xFF; st[12] = (g_clutchOut[0] >> 8) & 0xFF;
    st[13] = g_clutchRaw[1] & 0xFF; st[14] = (g_clutchRaw[1] >> 8) & 0xFF;
    st[15] = g_clutchOut[1] & 0xFF; st[16] = (g_clutchOut[1] >> 8) & 0xFF;
    // [17..20] deltas de encoder (i8), depois zera o acumulado
    st[17] = (uint8_t)g_encDelta[0]; st[18] = (uint8_t)g_encDelta[1];
    st[19] = (uint8_t)g_encDelta[2]; st[20] = (uint8_t)g_encDelta[3];
    for (int i = 0; i < 4; i++) g_encDelta[i] = 0;
    g_hid.sendReport(RID_STATE, st, PAYLOAD);
  }

  delay(2);
}
