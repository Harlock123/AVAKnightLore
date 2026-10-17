#!/usr/bin/env python3
"""Synthesises AVAKnightLore's sound effects and tunes into ../src/AVAKnightLore/Sounds as 16-bit mono WAV.

Nothing is sampled from any game: every effect is built here from square, triangle and noise voices. The
title tune is "Greensleeves" (traditional, long in the public domain) and the castle's tune a new composition
in the same old mode, both arranged for two voices. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave



RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAKnightLore", "Sounds")
RNG = random.Random(1984)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out




def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    write("Jump.wav", tone(0.22, 260, 620, "square", attack=0.002, release=0.6, gain=0.5), 0.12)
    write("Land.wav", tone(0.05, 160, 80, "square", attack=0.001, release=1.2, gain=0.5), 0.1)
    write("Pickup.wav", cat(*[tone(0.06, hz(n), kind="pulse", release=0.3) for n in ["A5", "C6", "E6", "A6"]]), 0.26)
    write("Cauldron.wav", cat(mix(tone(0.6, 300, 120, "noise", attack=0.05, release=0.5, gain=0.5), tone(0.5, 180, 90, "sine", release=0.6)),
                              *[tone(0.09, hz(n), kind="tri", release=0.3) for n in ["A4", "C5", "E5", "A5", "E5", "A5"]]), 0.36)
    write("Cured.wav", cat(*[tone(0.12, hz(n), kind="pulse", release=0.3) for n in ["A4", "C5", "E5", "A5", "C6", "E6"]], tone(0.8, hz("A6"), kind="pulse", vibrato=0.02, release=0.6)), 0.38)
    write("Door.wav", cat(tone(0.06, 200, 140, "square", attack=0.001, release=1.0, gain=0.4), silence(0.03), tone(0.06, 160, 110, "square", release=1.0, gain=0.4)), 0.14)
    write("Die.wav", cat(*[tone(0.07, 900 - i * 60, kind="square", release=0.3, gain=0.6) for i in range(12)], tone(0.4, 120, 60, "square", release=0.6, gain=0.6)), 0.32)
    # Dusk: a howl. Dawn: a bright chime.
    write("Dusk.wav", tone(1.2, 300, 520, "saw", attack=0.2, vibrato=0.06, vib_rate=6, release=0.4, gain=0.6), 0.32)
    write("Dawn.wav", cat(*[tone(0.15, hz(n), kind="sine", release=0.5) for n in ["E5", "A5", "C#6", "E6"]]), 0.3)
    write("DayCount.wav", tone(0.05, 1200, kind="square", attack=0.001, release=1.0, gain=0.4), 0.1)
    write("ExtraLife.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["G5", "B5", "D6", "G6", "D6", "G6"]]), 0.3)

    # Greensleeves (traditional), in A minor, three to the bar.
    gs = [("A4", 1), ("C5", 2), ("D5", 1), ("E5", 1.5), ("F5", 0.5), ("E5", 1), ("D5", 2), ("B4", 1), ("G4", 1.5), ("A4", 0.5), ("B4", 1),
          ("C5", 2), ("A4", 1), ("A4", 1.5), ("G#4", 0.5), ("A4", 1), ("B4", 2), ("G#4", 1), ("E4", 2), ("A4", 1),
          ("C5", 2), ("D5", 1), ("E5", 1.5), ("F5", 0.5), ("E5", 1), ("D5", 2), ("B4", 1), ("G4", 1.5), ("A4", 0.5), ("B4", 1),
          ("C5", 1.5), ("B4", 0.5), ("A4", 1), ("G#4", 1.5), ("F#4", 0.5), ("G#4", 1), ("A4", 3), ("A4", 2), ("-", 1)]
    gb = [("A2", 3), ("A2", 3), ("G2", 3), ("G2", 3), ("A2", 3), ("A2", 3), ("E2", 3), ("E2", 3),
          ("A2", 3), ("A2", 3), ("G2", 3), ("G2", 3), ("A2", 3), ("E2", 3), ("A2", 3), ("A2", 3)]
    write("TitleTune.wav", cat(tune(150, gs, gb, kind="tri"), silence(0.8)), 0.36)
    # The castle (an original composition in the same mode): slow and watchful.
    e = 0.5
    lead = [("E4", 1), ("A4", e), ("B4", e), ("C5", 1), ("B4", 1), ("A4", 2), ("G4", 1), ("E4", 1),
            ("D4", 1), ("E4", e), ("F4", e), ("E4", 2), ("C4", 1), ("D4", 1), ("E4", 2), ("-", 2)]
    bass = [("A2", 4), ("F2", 4), ("D2", 4), ("E2", 4)]
    write("Music.wav", tune(100, lead * 2, bass * 2, kind="pulse"), 0.22)
    write("Victory.wav", tune(150, [("A4", 0.5), ("C#5", 0.5), ("E5", 0.5), ("A5", 1.5), ("G#5", 0.5), ("A5", 0.5), ("B5", 0.5), ("C#6", 3)],
                              [("A2", 1), ("E2", 1), ("F#2", 1), ("D2", 1), ("E2", 1), ("A2", 2.5)], kind="pulse"), 0.4)
    write("GameOver.wav", tune(66, [("E5", 1), ("C5", 1), ("B4", 1), ("G#4", 1), ("A4", 4)], [("A2", 2), ("F2", 2), ("E2", 2), ("A1", 2)], kind="tri"), 0.42)


if __name__ == "__main__":
    main()
