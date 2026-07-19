#!/usr/bin/env python3
"""
Validador de descritor HID PID Force Feedback (host-side).

Valida se um report descriptor HID é bem-formado:
- Collections balanceadas
- Contém uso Joystick (Usage Page 0x01, Usage 0x04)
- Contém uso PID/Force Feedback (Usage Page 0x0F)

Uso:
    python3 validate_ffb_descriptor.py --self-test
    python3 validate_ffb_descriptor.py <arquivo.bin>
"""


def validate(desc: bytes) -> dict:
    """
    Valida um HID report descriptor.

    Args:
        desc: bytes do descriptor HID

    Returns:
        dict com chaves:
        - balanced (bool): True se collections balanceadas (depth retorna 0, nunca negativo)
        - has_joystick (bool): True se encontrou Usage Page 0x01 + Usage 0x04
        - has_pid (bool): True se encontrou Usage Page 0x0F
        - end_depth (int): profundidade final (0 se balanceado)
        - errors (list): lista de erros encontrados
    """
    balanced = False
    has_joystick = False
    has_pid = False
    end_depth = 0
    errors = []

    depth = 0
    last_usage_page = None
    i = 0

    while i < len(desc):
        prefix = desc[i]
        i += 1

        # Long item (0xFE) - skip
        if prefix == 0xFE:
            if i + 1 >= len(desc):
                errors.append("Long item sem tamanho de dados")
                break
            data_len = desc[i]
            i += 1 + data_len
            continue

        # Short item
        size_code = prefix & 0x03  # bits 0-1
        item_type = (prefix >> 2) & 0x03  # bits 2-3
        tag = (prefix >> 4) & 0x0F  # bits 4-7

        # Map size code to actual data bytes
        size_map = {0: 0, 1: 1, 2: 2, 3: 4}
        data_size = size_map.get(size_code, 0)

        if i + data_size > len(desc):
            errors.append(f"Item truncado: esperava {data_size} bytes, apenas {len(desc) - i} disponíveis")
            break

        data = desc[i:i+data_size]
        i += data_size

        # Parse item based on type and tag
        if item_type == 1:  # Global
            if tag == 0:  # Usage Page (Global, tag 0)
                if data_size >= 1:
                    last_usage_page = data[0]
        elif item_type == 2:  # Local
            if tag == 0:  # Usage (Local, tag 0)
                if data_size >= 1 and last_usage_page is not None:
                    usage = data[0]
                    # Check for Joystick (Usage Page 0x01, Usage 0x04)
                    if last_usage_page == 0x01 and usage == 0x04:
                        has_joystick = True

        # Check for PID/Force Feedback usage page
        if item_type == 1 and tag == 0:  # Usage Page (Global)
            if data_size >= 1 and data[0] == 0x0F:
                has_pid = True

        # Main items
        if item_type == 0:  # Main
            if tag == 10:  # Collection (Main, tag 10)
                depth += 1
            elif tag == 12:  # End Collection (Main, tag 12)
                depth -= 1
                if depth < 0:  # fecha mais do que abriu → desbalanceado
                    errors.append("End Collection sem Collection aberta (depth < 0)")

    end_depth = depth

    # Validate balance: depth zera no fim E nunca ficou negativo E sem truncamento
    if depth == 0 and not errors:
        balanced = True

    return {
        "balanced": balanced,
        "has_joystick": has_joystick,
        "has_pid": has_pid,
        "end_depth": end_depth,
        "errors": errors,
    }


if __name__ == "__main__":
    import sys

    if len(sys.argv) > 1 and sys.argv[1] == "--self-test":
        # Test case GOOD: joystick + collection balanceada + usage page PID
        GOOD = bytes([0x05,0x01, 0x09,0x04, 0xA1,0x01, 0x05,0x0F, 0x09,0x21, 0xC0])

        # Test case BAD: o bug do shim - truncado, application collection nunca fecha
        BAD = bytes([0x05,0x01, 0x09,0x04, 0xA1,0x01, 0x09,0x01])

        # Run tests
        print("Running self-tests...")
        print()

        result_good = validate(GOOD)
        print("GOOD descriptor:")
        print(f"  Input: {GOOD.hex()}")
        print(f"  Result: {result_good}")
        print()

        # Assertions for GOOD
        assert result_good["balanced"] == True, f"GOOD: balanced should be True, got {result_good['balanced']}"
        assert result_good["has_joystick"] == True, f"GOOD: has_joystick should be True, got {result_good['has_joystick']}"
        assert result_good["has_pid"] == True, f"GOOD: has_pid should be True, got {result_good['has_pid']}"
        assert result_good["end_depth"] == 0, f"GOOD: end_depth should be 0, got {result_good['end_depth']}"
        assert result_good["errors"] == [], f"GOOD: errors should be empty, got {result_good['errors']}"
        print("✓ GOOD descriptor passed all assertions")
        print()

        result_bad = validate(BAD)
        print("BAD descriptor:")
        print(f"  Input: {BAD.hex()}")
        print(f"  Result: {result_bad}")
        print()

        # Assertions for BAD
        assert result_bad["balanced"] == False, f"BAD: balanced should be False, got {result_bad['balanced']}"
        assert result_bad["has_joystick"] == True, f"BAD: has_joystick should be True, got {result_bad['has_joystick']}"
        assert result_bad["has_pid"] == False, f"BAD: has_pid should be False, got {result_bad['has_pid']}"
        assert result_bad["end_depth"] == 1, f"BAD: end_depth should be 1, got {result_bad['end_depth']}"
        print("✓ BAD descriptor passed all assertions")
        print()

        print("✓✓✓ All tests passed!")
    else:
        print("Usage: python3 validate_ffb_descriptor.py --self-test")
