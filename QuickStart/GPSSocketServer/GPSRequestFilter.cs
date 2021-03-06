﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SuperSocket.Common;
using SuperSocket.SocketBase;
using SuperSocket.SocketBase.Command;
using SuperSocket.SocketBase.Protocol;

namespace SuperSocket.QuickStart.GPSSocketServer
{
    /// <summary>
    /// It is the kind of protocol that
    /// the first two bytes of each command are { 0x68, 0x68 }
    /// and the last two bytes of each command are { 0x0d, 0x0a }
    /// and the 16th byte (data[15]) of each command indicate the command type
    /// if data[15] = 0x10, the command is a keep alive one
    /// if data[15] = 0x1a, the command is position one
    /// </summary>
    class GPSRequestFilter : RequestFilterBase<BinaryRequestInfo>
    {
        private static byte[] m_StartMark = new byte[] { 0x68, 0x68 };
        private static byte[] m_EndMark = new byte[] { 0x0d, 0x0a };

        private SearchMarkState<byte> m_StartSearchState;
        private SearchMarkState<byte> m_EndSearchState;

        private bool m_FoundStart = false;

        public GPSRequestFilter()
        {
            m_StartSearchState = new SearchMarkState<byte>(m_StartMark);
            m_EndSearchState = new SearchMarkState<byte>(m_EndMark);
        }

        public override BinaryRequestInfo Filter(IAppSession session, byte[] readBuffer, int offset, int length, bool toBeCopied, out int left)
        {
            left = 0;

            if (!m_FoundStart)
            {
                var pos = readBuffer.SearchMark(offset, length, m_StartSearchState);

                //Don't cache invalid data
                if (pos < 0)
                    return null;

                //Found start mark
                m_FoundStart = true;

                int searchEndMarkOffset = pos + m_StartMark.Length;

                //The end mark could not exist in this round received data
                if (offset + length <= searchEndMarkOffset)
                {
                    AddArraySegment(m_StartMark, 0, m_StartMark.Length, false);
                    return null;
                }

                int searchEndMarkLength = offset + length - searchEndMarkOffset;

                var endPos = readBuffer.SearchMark(searchEndMarkOffset, searchEndMarkLength, m_EndSearchState);

                if (endPos < 0)
                {
                    AddArraySegment(readBuffer, pos, length + offset - pos, toBeCopied);
                    return null;
                }

                int parsedLen = endPos - pos + m_EndMark.Length;
                left = length - parsedLen;

                var requestInfo = CreateRequestInfo(readBuffer.CloneRange(pos, parsedLen));

                Reset();

                return requestInfo;
            }
            else
            {
                var endPos = readBuffer.SearchMark(offset, length, m_EndSearchState);
                //Haven't found end mark
                if (endPos < 0)
                {
                    AddArraySegment(readBuffer, offset, length, toBeCopied);
                    return null;
                }

                //Found end mark
                int parsedLen = endPos - offset + m_EndMark.Length;
                left = length - parsedLen;

                byte[] commandData = new byte[BufferSegments.Count + parsedLen];

                if (BufferSegments.Count > 0)
                    BufferSegments.CopyTo(commandData, 0, 0, BufferSegments.Count);

                Array.Copy(readBuffer, offset, commandData, BufferSegments.Count, parsedLen);

                var requestInfo = CreateRequestInfo(commandData);

                Reset();

                return requestInfo;
            }
        }

        public override void Reset()
        {
            m_StartSearchState.Matched = 0;
            m_EndSearchState.Matched = 0;
            m_FoundStart = false;

            base.Reset();
        }

        private BinaryRequestInfo CreateRequestInfo(byte[] data)
        {
            return new BinaryRequestInfo(BitConverter.ToString(data, 15, 1), data);
        }
    }
}
