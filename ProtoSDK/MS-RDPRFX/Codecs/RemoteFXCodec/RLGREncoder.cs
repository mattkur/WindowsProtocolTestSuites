// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using System;
using System.Collections.Generic;

namespace Microsoft.Protocols.TestTools.StackSdk.RemoteDesktop.Rdprfx
{
    /// <summary>
    /// The RLGR encoder
    /// </summary>
    public class RLGREncoder
    {
        // Constants used within the RLGR1/RLGR3 algorithm

        const int KPMAX = 80;  // max value for kp or krp
        const int LSGR = 3;  // shift count to convert kp to k
        const int UP_GR = 4;  // increase in kp after a zero run in RL mode
        const int DN_GR = 6;  // decrease in kp after a nonzero symbol in RL mode
        const int UQ_GR = 3;   // increase in kp after nonzero symbol in GR mode
        const int DQ_GR = 3;   // decrease in kp after zero symbol in GR mode

        short[] inputData;
        int nextInputIdx;
        int bufferOffset = 0;
        byte[] pBuffer;

        /// <summary>
        /// Do ALGR encode to the input data.
        /// </summary>
        /// <param name="inputArr">Input data to be encoded.</param>
        /// <param name="mode">The ALGR mode, can be RLGR1 or RLGR3.</param>
        /// <returns>The encoded data.</returns>
        public byte[] Encode(short[] inputArr, EntropyAlgorithm mode)
        {
            inputData = inputArr;
            nextInputIdx = 0;
            bufferOffset = 0; //offset&0xFFFFFFF8 = byte offset, offset&0x7 = bit offset
            pBuffer = new byte[inputArr.Length];

            RLGR_Encode(mode);
            int numbytes = bufferOffset >> 3;
            int bitOffset = bufferOffset & 7;
            if (bitOffset != 0) numbytes++;

            byte[] encodedBytes = new byte[numbytes];
            Array.Copy(pBuffer, encodedBytes, encodedBytes.Length);
            return encodedBytes;
        }

        //
        // Returns the next coefficient (a signed int) to encode, from the input stream
        //
        int GetNextInput()
        {
            return (int)inputData[nextInputIdx++];
        }

        bool hasMoreData()
        {
            return (nextInputIdx <= inputData.Length - 1);
        }

        //
        // Emit bitPattern to the output bitstream.
        // The bitPattern value represents a bit sequence that is generated by shifting 
        // new bits in from the right. If we take the binary representation of bitPattern, 
        // with N(numBits-1) being the leftmost bit position and 0 being the rightmost bit position, 
        // the mapping of bitPattern to the output bytes is as follows:
        //
        //     bitPattern[N..0] -> byte[MSB..LSB] .. byte[MSB..LSB]
        //
        public void OutputBits(
            int numBits,      // number of bits in bitPattern
            int bitPattern   // bit pattern
            )
        {
            int patternOffset = numBits - 1;

            while (patternOffset >= 0)
            {
                int bit = ((bitPattern & (1 << patternOffset)) != 0) ? 1 : 0;
                OutputBit(1, bit);
                patternOffset--;
            }
        }

        //
        // Emit a bit (0 or 1), count number of times, to the output bitstream
        //
        void OutputBit(
            int count,     // number of times to emit the bit
            int bit        // 0 or 1
            )
        {
            if (count == 0) return;

            for (int i = 0; i < count; i++)
            {
                int bitOffset = bufferOffset & 7;
                int bufferBoundary = bufferOffset >> 3;
                if (bit != 0) // bit 1
                {
                    pBuffer[bufferBoundary] |= (byte)(1 << (7 - bitOffset));
                }
                else
                {
                    pBuffer[bufferBoundary] &= (byte)(0xFF - ((byte)(1 << (7 - bitOffset))));
                }
                bufferOffset++;
            }
        }

        //
        // Returns the least number of bits required to represent a given value
        // 
        uint GetMinBits(
            int val      // returns ceil(log2(val))
            )
        {
            uint m1 = (uint)Math.Ceiling(Math.Log(val, 2));
            while ((val >> (int)m1) != 0)
            {
                m1++;
            }
            return m1;
        }

        // 
        // Converts the input value to (2 * abs(input) - sign(input)), 
        // where sign(input) = (input < 0 ? 1 : 0) and returns it
        //
        uint Get2MagSign(
            int input    // input value
            )
        {
            uint output = (uint)(2 * Math.Abs(input));
            if (input < 0) output -= 1;
            return output;
        }


        //
        // Update the passed parameter and clamp it to the range [0,KPMAX]
        // Return the value of parameter right-shifted by LSGR
        //
        int UpdateParam(
            ref int param,    // parameter to update
            int deltaP    // update delta
            )
        {
            param += deltaP;
            if (param > KPMAX) param = KPMAX;
            if (param < 0) param = 0;
            return (param >> LSGR);
        }


        //
        // Outputs the Golomb/Rice encoding of a non-negative integer
        //
        void
        CodeGR(
            ref int krp,   // GR parameter, used and updated based on the input value
            uint val     // input non-negative value to be encoded
            )
        {
            int kr = krp >> LSGR;

            // unary part of GR code

            uint vk = val >> kr;
            OutputBit((int)vk, 1);
            OutputBit(1, 0);

            // remainder part of GR code, if needed
            if (kr != 0)
            {
                OutputBits(kr, (int)(val & ((1 << kr) - 1)));
            }

            // update krp, only if it is not equal to 1
            if (vk == 0)
            {
                UpdateParam(ref krp, -2);
            }
            else if (vk > 1)
            {
                UpdateParam(ref krp, (int)vk);
            }
        }


        //
        // Routine that outputs a stream of RLGR1/RLGR3-encoded bits
        //
        void RLGR_Encode(
            EntropyAlgorithm rlgrMode    // RLGR1 || RLGR3
            )
        {
            // initialize the parameters
            int k = 1;
            int kp = 1 << LSGR;
            //int kr = 1;
            int krp = 1 << LSGR;

            // process all the input coefficients
            while (hasMoreData())
            {
                int input;

                if (k != 0)
                {
                    // RUN-LENGTH MODE

                    // collect the run of zeros in the input stream
                    int numZeros = 0;
                    while ((input = GetNextInput()) == 0)
                    {
                        ++numZeros;
                        if (!hasMoreData()) break;
                    }

                    // emit output zebros
                    int runmax = 1 << k;
                    while (numZeros >= runmax)
                    {
                        OutputBit(1, 0);             // output a zero bit
                        numZeros -= runmax;
                        k = UpdateParam(ref kp, UP_GR);  // update kp, k 
                        runmax = 1 << k;
                    }

                    // output a 1 to terminate runs
                    OutputBit(1, 1);

                    // output the remaining run length using k bits
                    OutputBits(k, numZeros);

                    if (input != 0)
                    {
                        // encode the nonzero value using GR coding

                        int mag = Math.Abs(input);            // absolute value of input coefficient
                        int sign = (input < 0 ? 1 : 0);  // sign of input coefficient

                        OutputBit(1, sign);      // output the sign bit
                        CodeGR(ref krp, (uint)(mag - 1));   // output GR code for (mag - 1)

                        k = UpdateParam(ref kp, -DN_GR);
                    }
                }
                else
                {
                    // GOLOMB-RICE MODE

                    if (rlgrMode == EntropyAlgorithm.CLW_ENTROPY_RLGR1)
                    {
                        // RLGR1 variant

                        // convert input to (2*magnitude - sign), encode using GR code
                        uint twoMs = Get2MagSign(GetNextInput());
                        CodeGR(ref krp, twoMs);

                        // update k, kp
                        if (twoMs == 0)
                        {
                            k = UpdateParam(ref kp, UQ_GR);
                        }
                        else
                        {
                            k = UpdateParam(ref kp, -DQ_GR);
                        }
                    }
                    else // rlgrMode == RLGR3
                    {
                        // RLGR3 variant

                        // convert the next two input values to (2*magnitude - sign) and
                        // encode their sum using GR code

                        uint twoMs1 = Get2MagSign(GetNextInput());
                        uint twoMs2 = 0;
                        if (hasMoreData())
                        {
                            twoMs2 = Get2MagSign(GetNextInput());
                        }

                        uint sum2Ms = twoMs1 + twoMs2;

                        CodeGR(ref krp, sum2Ms);

                        // encode binary representation of the first input (twoMs1).  
                        OutputBits((int)GetMinBits((int)sum2Ms), (int)twoMs1);

                        // update k,kp for the two input values
                        if (twoMs1 != 0 && twoMs2 != 0)
                        {
                            k = UpdateParam(ref kp, -2 * DQ_GR);
                        }
                        else if (twoMs1 == 0 && twoMs2 == 0)
                        {
                            k = UpdateParam(ref kp, 2 * UQ_GR);
                        }
                    }
                }
            }
        }

    }
}
