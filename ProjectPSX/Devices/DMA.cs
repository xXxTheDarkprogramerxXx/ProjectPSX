﻿
using System;

namespace ProjectPSX.Devices {
    public class DMA {

        public abstract class AChannel {
            public abstract void write(uint register, uint value);
            public abstract uint load(uint regiter);
            public abstract void setDMA_Transfer(DMA_Transfer dma_transfer);
        }

        private class InterruptChannel : AChannel {

            private uint control;

            private bool forceIRQ;
            private uint irqEnable;
            private bool masterEnable;
            private uint irqFlag;
            private bool masterFlag;

            private bool edgeInterruptTrigger;

            public InterruptChannel() {
                control = 0x07654321;
            }
            public override uint load(uint register) {
                switch (register) {
                    case 0: return control;
                    case 4: return loadInterrupt();
                    case 6: return loadInterrupt() >> 16; //castlevania symphony of the night and dino crisis 2 ask for this
                    default: Console.WriteLine("Unhandled register on interruptChannel DMA load " + register); return 0xFFFF_FFFF;
                }
            }

            private uint loadInterrupt() {
                uint interruptRegister = 0;

                interruptRegister |= (forceIRQ ? 1u : 0) << 15;
                interruptRegister |= irqEnable << 16;
                interruptRegister |= (masterEnable ? 1u : 0) << 23;
                interruptRegister |= irqFlag << 24;
                interruptRegister |= (masterFlag ? 1u : 0) << 31;

                return interruptRegister;
            }

            public override void write(uint register, uint value) {
                //Console.WriteLine("irqflag pre: " + irqFlag.ToString("x8"));
                switch (register) {
                    case 0: control = value; break;
                    case 4: writeInterrupt(value); break;
                    case 6: writeInterrupt(value << 16 | (forceIRQ ? 1u : 0) << 15); break;
                    default: Console.WriteLine("Unhandled write on DMA register" + register); break;
                }
                //Console.WriteLine("irqflag post: " + irqFlag.ToString("x8"));
            }

            private void writeInterrupt(uint value) {
                forceIRQ = ((value >> 15) & 0x1) != 0;
                irqEnable = (value >> 16) & 0x7F;
                masterEnable = ((value >> 23) & 0x1) != 0;
                irqFlag &= ~((value >> 24) & 0x7F);

                masterFlag = updateMasterFlag();
            }

            public override void setDMA_Transfer(DMA_Transfer dma_transfer) {
                //throw new NotImplementedException();
            }

            public void handleInterrupt(int channel) {
                //IRQ flags in Bit(24 + n) are set upon DMAn completion - but caution - they are set ONLY if enabled in Bit(16 + n).
                if ((irqEnable & (1 << channel)) != 0) {
                    irqFlag |= (uint)(1 << channel);
                }

                //Console.WriteLine("MasterFlag" + masterFlag + " irqEnable16 " + irqEnable.ToString("x8") + " irqFlag24 " + irqFlag.ToString("x8") + forceIRQ +  " "  + masterEnable + " " +  ((irqEnable & irqFlag) > 0));

                bool prevMasterFlag = masterFlag;
                masterFlag = updateMasterFlag();
                if (masterFlag && !prevMasterFlag) {
                    edgeInterruptTrigger = true;
                }
            }

            private bool updateMasterFlag() {
                //Bit31 is a simple readonly flag that follows the following rules:
                //IF b15 = 1 OR(b23 = 1 AND(b16 - 22 AND b24 - 30) > 0) THEN b31 = 1 ELSE b31 = 0
                return forceIRQ || (masterEnable && ((irqEnable & irqFlag) > 0));
            }

            public bool tick() {
                if (edgeInterruptTrigger) {
                    edgeInterruptTrigger = false;
                    //Console.WriteLine("[IRQ] Triggering DMA");
                    return true;
                }
                return false;
            }
        }

        private class Channel : AChannel {

            private uint baseAddress;
            private uint blockSize;
            private uint blockCount;

            private uint transferDirection;
            private uint memoryStep;
            private uint choppingEnable;
            private uint syncMode;
            private uint choppingDMAWindowSize;
            private uint choppingCPUWindowSize;
            private bool enable;
            private bool trigger;

            private DMA_Transfer dma_transfer;
            private InterruptChannel interrupt;
            private int channelNumber;

            public Channel(int channelNumber, InterruptChannel interrupt) {
                this.channelNumber = channelNumber;
                this.interrupt = interrupt;
            }

            public override uint load(uint register) {
                switch (register) {
                    case 0: return baseAddress;
                    case 4: return blockCount << 16 | blockSize;
                    case 8: return loadChannelControl();
                    default: return 0;
                }
            }

            private uint loadChannelControl() {
                uint channelControl = 0;

                channelControl |= transferDirection;
                channelControl |= (memoryStep == 4 ? 0 : 1u) << 1;
                channelControl |= choppingEnable << 8;
                channelControl |= syncMode << 9;
                channelControl |= choppingDMAWindowSize << 16;
                channelControl |= choppingCPUWindowSize << 20;
                channelControl |= (enable ? 1u : 0) << 24;
                channelControl |= (trigger ? 1u : 0) << 28;

                return channelControl;
            }

            public override void write(uint register, uint value) {
                switch (register) {
                    case 0: baseAddress = value & 0xFFFFFF; break;
                    case 4: blockCount = value >> 16; blockSize = value & 0xFFFF; break;
                    case 8: writeChannelControl(value); break;
                    default: Console.WriteLine("Unhandled Write on register " + register); break;
                }
            }

            public override void setDMA_Transfer(DMA_Transfer dma_transfer) {
                this.dma_transfer = dma_transfer;
            }

            private void writeChannelControl(uint value) {
                transferDirection = value & 0x1;
                memoryStep = (uint)(((value >> 1) & 0x1) == 0 ? 4 : -4);
                choppingEnable = (value >> 8) & 0x1;
                syncMode = (value >> 9) & 0x3;
                choppingDMAWindowSize = (value >> 16) & 0x7;
                choppingCPUWindowSize = (value >> 20) & 0x7;
                enable = ((value >> 24) & 0x1) != 0;
                trigger = ((value >> 28) & 0x1) != 0;

                handleDMA();
            }

            private void handleDMA() {
                if (!isActive()) return;

                if (syncMode == 0) {
                    blockCopy(blockSize);
                } else if (syncMode == 1) {
                    blockCopy(blockSize * blockCount);
                } else if (syncMode == 2) {
                    linkedList();
                }

                //disable channel
                enable = false;
                trigger = false;

                interrupt.handleInterrupt(channelNumber);
            }


            private void blockCopy(uint size) {
                while (size > 0) {
                    switch (transferDirection) {
                        case 0: //To Ram
                            uint data = 0;
                            //byte[] cdTest = null;

                            switch (channelNumber) {
                                case 1: //MDECout
                                    //Console.WriteLine("[DMA] MdecOut to ram " + size);
                                    data = dma_transfer.fromMDECout();
                                    break;
                                case 2: //GPU
                                    data = dma_transfer.fromGPU();
                                    //Console.WriteLine("[DMA] [C2 GPU] Address: {0} Data: {1} Size {2}", (baseAddress & 0x1F_FFFC).ToString("x8"), data.ToString("x8"), size);
                                    break;
                                case 3: //CD
                                    data = dma_transfer.fromCD();
                                    //if(step == -4) {
                                    //    Console.WriteLine("WARNING !!! UNHANDLED REVERSE ON BUFFER CD TRANSFER");
                                    //    Console.ReadLine();
                                    //}
                                    //cdTest = dma_transfer.fromCD(size);
                                    //for (int i = 0; i < cdTest.Length; i++) {
                                    //    Console.WriteLine(cdTest[i].ToString("x2"));
                                    //}
                                    //dma_transfer.toRAM(dmaAddress & 0x1F_FFFC, cdTest, size);
                                    //return;
                                    //Console.WriteLine("[DMA] [C3 CD] TORAM Address: {0} Data: {1} Size {2}", (baseAddress & 0x1F_FFFC).ToString("x8"), data.ToString("x8"), size);
                                    break;
                                case 6: //OTC
                                    if (size == 1) {
                                        data = 0xFF_FFFF;
                                    } else {
                                        data = (baseAddress - 4) & 0xFF_FFFF;
                                    }
                                    //Console.WriteLine("[DMA] [C6 OTC] Address: {0} Data: {1}", (baseAddress & 0x1F_FFFC).ToString("x8"), data.ToString("x8"));
                                    break;
                                default:
                                    data = 0;
                                    //Console.WriteLine("[DMA] [BLOCK COPY] Unsupported Channel (to Ram) " + channelNumber);
                                    break;
                            }
                            dma_transfer.toRAM(baseAddress & 0x1F_FFFC, data);

                            break;
                        case 1: //From Ram
                                //Console.WriteLine("Size " + size);
                            uint[] load = dma_transfer.fromRAM(baseAddress & 0x1F_FFFC, size);

                            switch (channelNumber) {
                                case 0: //MDECin
                                    Console.WriteLine("[DMA] MDEC IN blockCopy " + size);
                                    dma_transfer.toMDECin(load);
                                    return;
                                case 2: //GPU
                                    dma_transfer.toGPU(load);
                                    return;
                                default: //MDECin and SPU
                                    //Console.WriteLine("[DMA] [BLOCK COPY] Unsupported Channel (from Ram) " + channelNumber);
                                    return;
                            }
                    }

                    baseAddress += memoryStep;
                    size--;
                }
            }

            private void linkedList() {
                uint header = 0;

                while ((header & 0x800000) == 0) {
                    //Console.WriteLine("HEADER addr " + baseAddress.ToString("x8"));
                    header = dma_transfer.fromRAM(baseAddress);
                    //Console.WriteLine("HEADER addr " + baseAddress.ToString("x8") + " value: " + header.ToString("x8"));
                    uint size = header >> 24;

                    if (size > 0) {
                        baseAddress = (baseAddress + 4) & 0x1ffffc;
                        //uint load = dma_transfer.fromRAM(dmaAddress);
                        // Console.WriteLine("GPU SEND addr " + dmaAddress.ToString("x8") + " value: " + load.ToString("x8"));
                        //dma_transfer.toGPU(load);
                        dma_transfer.toGPU(dma_transfer.fromRAM(baseAddress, size));
                    }
                    baseAddress = header & 0x1ffffc;
                }
            }

            private bool isActive() {
                if (syncMode == 0) { //0  Start immediately and transfer all at once (used for CDROM, OTC) needs TRIGGER
                    return enable && trigger;
                } else {
                    return enable;
                }
            }
        }

        AChannel[] channels = new AChannel[8];

        public DMA() {
            InterruptChannel interrupt = new InterruptChannel();
            channels[0] = new Channel(0, interrupt);
            channels[1] = new Channel(1, interrupt);
            channels[2] = new Channel(2, interrupt);
            channels[3] = new Channel(3, interrupt);
            channels[4] = new Channel(4, interrupt);
            channels[5] = new Channel(5, interrupt);
            channels[6] = new Channel(6, interrupt);
            channels[7] = interrupt;
        }

        public uint load(uint addr) {
            uint channel = (addr & 0x70) >> 4;
            uint register = addr & 0xF;
            //Console.WriteLine("DMA load " + channel + " " + register  + ":" + channels[channel].load(register).ToString("x8"));
            return channels[channel].load(register);
        }

        public void write(uint addr, uint value) {
            uint channel = (addr & 0x70) >> 4;
            uint register = addr & 0xF;
            //Console.WriteLine("DMA write " + channel + " " + register + ":" + value.ToString("x8"));

            channels[channel].write(register, value);
        }

        public bool tick() => ((InterruptChannel)channels[7]).tick();

        public void setDMA_Transfer(DMA_Transfer dma_transfer) {
            for (int i = 0; i < channels.Length; i++) {
                channels[i].setDMA_Transfer(dma_transfer);
            }
        }

    }
}
