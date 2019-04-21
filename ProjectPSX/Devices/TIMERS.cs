﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProjectPSX.Devices {
    public class TIMERS : Device {

        TIMER[] timer = new TIMER[3];

        public TIMERS() {
            timer[0] = new TIMER(0);
            timer[1] = new TIMER(1);
            timer[2] = new TIMER(2);
        }

        public new void write(Width w, uint addr, uint value) {
            int timerNumber = (int)(addr & 0xF0) >> 4;
            timer[timerNumber].write(w, addr, value);
        }

        public new uint load(Width w, uint addr) {
            int timerNumber = (int)(addr & 0xF0) >> 4;
            return timer[timerNumber].load(w, addr);
        }

        public void tick(ushort cycles) {
            timer[0].tick(cycles);
            timer[1].tick(cycles);
            timer[2].tick(cycles);
        }

        private class TIMER {
            private int timerNumber;

            private ushort counterValue;
            private uint counterTargetValue;

            private byte counter2div8;

            private byte syncEnable;
            private byte syncMode;
            private byte resetCounterOnTarget;
            private byte irqWhenCounterTarget;
            private byte irqWhenCounterFFFF;
            private byte irqRepeat;
            private byte irqPulse;
            private byte clockSource;
            private byte interruptRequest;
            private byte reachedTarget;
            private byte reachedFFFF;

            public TIMER(int timerNumber) {
                this.timerNumber = timerNumber;
            }

            public void write(Width w, uint addr, uint value) {
                switch (addr & 0xF) {
                    case 0x0: counterValue = (ushort)value; break;
                    case 0x4: setCounterMode(value); break;
                    case 0x8: counterTargetValue = value; break;
                }
            }

            public uint load(Width w, uint addr) {
                switch (addr & 0xF) {
                    case 0x0: return (uint)counterValue;
                    case 0x4: return getCounterMode();
                    case 0x8: return counterTargetValue;
                    default: return 0;
                }
            }

            public void tick(ushort cycles) { //todo this needs rework

                counterValue += cycles;

            }

            private void setCounterMode(uint value) {
                syncEnable = (byte)(value & 0x1);
                syncMode = (byte)((value >> 1) & 0x3);
                resetCounterOnTarget = (byte)((value >> 3) & 0x1);
                irqWhenCounterTarget = (byte)((value >> 4) & 0x1);
                irqWhenCounterFFFF = (byte)((value >> 5) & 0x1);
                irqRepeat = (byte)((value >> 6) & 0x1);
                irqPulse = (byte)((value >> 7) & 0x1);
                clockSource = (byte)((value >> 8) & 0x3);
                interruptRequest = (byte)((value >> 10) & 0x1);
                reachedTarget = (byte)((value >> 11) & 0x1);
                reachedFFFF = (byte)((value >> 12) & 0x1);
            }

            private uint getCounterMode() {
                uint counterMode = 0;
                counterMode |= syncEnable;
                counterMode |= (uint)syncMode << 1;
                counterMode |= (uint)resetCounterOnTarget << 3;
                counterMode |= (uint)irqWhenCounterTarget << 4;
                counterMode |= (uint)irqWhenCounterFFFF << 5;
                counterMode |= (uint)irqRepeat << 6;
                counterMode |= (uint)irqPulse << 7;
                counterMode |= (uint)clockSource << 8;
                counterMode |= (uint)interruptRequest << 10;
                counterMode |= (uint)reachedTarget << 11;
                counterMode |= (uint)reachedFFFF << 12;

                reachedTarget = 0;
                reachedFFFF = 0;

                return counterMode;
            }
        }
    }
}
