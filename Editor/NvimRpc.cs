using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace NvimUnity
{
    /// <summary>
    /// Minimal msgpack-rpc client for Neovim's API over TCP (nvim --listen 127.0.0.1:PORT).
    /// Talking to nvim directly is much faster than spawning `nvim --server ... --remote-send`
    /// (especially through wsl.exe) and works the same for native and WSL instances.
    /// </summary>
    internal static class NvimRpc
    {
        public enum Status
        {
            /// <summary>Nothing is listening on the port.</summary>
            NotRunning,
            /// <summary>The request succeeded.</summary>
            Ok,
            /// <summary>nvim answered with an error.</summary>
            Error,
            /// <summary>Something is listening but did not answer in time (nvim busy / blocked on a prompt).</summary>
            Timeout,
        }

        static int s_MsgId;

        public static Status ExecLua(int port, string code, object[] args, out string error, int connectTimeoutMs = 300, int replyTimeoutMs = 2000)
        {
            error = null;
            using (var client = new TcpClient(AddressFamily.InterNetwork))
            {
                try
                {
                    IAsyncResult ar = client.BeginConnect(IPAddress.Loopback, port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(connectTimeoutMs))
                        return Status.NotRunning;
                    client.EndConnect(ar);
                }
                catch (Exception)
                {
                    return Status.NotRunning;
                }

                client.NoDelay = true;
                NetworkStream stream = client.GetStream();
                stream.WriteTimeout = replyTimeoutMs;
                stream.ReadTimeout = replyTimeoutMs;

                int id = ++s_MsgId & 0x7fffffff;
                byte[] request;
                using (var ms = new MemoryStream())
                {
                    var w = new Writer(ms);
                    w.ArrayHeader(4);
                    w.Int(0); // request
                    w.Int(id);
                    w.Str("nvim_exec_lua");
                    w.ArrayHeader(2);
                    w.Str(code);
                    w.ArrayHeader(args?.Length ?? 0);
                    if (args != null)
                        foreach (object a in args) w.Value(a);
                    request = ms.ToArray();
                }

                try
                {
                    stream.Write(request, 0, request.Length);
                    stream.Flush();
                }
                catch (Exception)
                {
                    return Status.NotRunning;
                }

                try
                {
                    var reader = new Reader(stream);
                    while (true)
                    {
                        if (!(reader.ReadValue() is List<object> msg) || msg.Count < 3) continue;
                        long type = Convert.ToInt64(msg[0]);
                        if (type != 1 || msg.Count < 4) continue; // skip notifications/requests
                        if (Convert.ToInt64(msg[1]) != id) continue;
                        if (msg[2] == null) return Status.Ok;
                        error = DescribeError(msg[2]);
                        return Status.Error;
                    }
                }
                catch (EndOfStreamException)
                {
                    // Connection accepted then closed (e.g. a stale WSL port relay) -> nobody is really there.
                    return Status.NotRunning;
                }
                catch (IOException e) when (e.InnerException is SocketException se && se.SocketErrorCode == SocketError.TimedOut)
                {
                    return Status.Timeout;
                }
                catch (IOException)
                {
                    return Status.NotRunning;
                }
            }
        }

        static string DescribeError(object err)
        {
            if (err is List<object> list && list.Count >= 2) return Convert.ToString(list[1]);
            return Convert.ToString(err);
        }

        // ------------------------------------------------------------------ msgpack writer

        sealed class Writer
        {
            readonly Stream m_S;
            public Writer(Stream s) => m_S = s;

            void B(int b) => m_S.WriteByte((byte)b);

            void BE(ulong v, int bytes)
            {
                for (int i = bytes - 1; i >= 0; i--) B((int)((v >> (8 * i)) & 0xff));
            }

            public void ArrayHeader(int n)
            {
                if (n < 16) B(0x90 | n);
                else if (n <= 0xffff) { B(0xdc); BE((ulong)n, 2); }
                else { B(0xdd); BE((ulong)n, 4); }
            }

            public void Int(long v)
            {
                if (v >= 0 && v < 128) B((int)v);
                else if (v < 0 && v >= -32) B((int)(0xe0 | (v + 32)));
                else { B(0xd3); BE((ulong)v, 8); }
            }

            public void Str(string s)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(s ?? "");
                int n = bytes.Length;
                if (n < 32) B(0xa0 | n);
                else if (n <= 0xff) { B(0xd9); B(n); }
                else if (n <= 0xffff) { B(0xda); BE((ulong)n, 2); }
                else { B(0xdb); BE((ulong)n, 4); }
                m_S.Write(bytes, 0, n);
            }

            public void Value(object o)
            {
                switch (o)
                {
                    case null: B(0xc0); break;
                    case bool b: B(b ? 0xc3 : 0xc2); break;
                    case int i: Int(i); break;
                    case long l: Int(l); break;
                    case string s: Str(s); break;
                    default: Str(o.ToString()); break;
                }
            }
        }

        // ------------------------------------------------------------------ msgpack reader

        sealed class Reader
        {
            readonly Stream m_S;
            public Reader(Stream s) => m_S = s;

            int B()
            {
                int b = m_S.ReadByte();
                if (b < 0) throw new EndOfStreamException();
                return b;
            }

            byte[] Bytes(long n)
            {
                var buf = new byte[n];
                int read = 0;
                while (read < n)
                {
                    int r = m_S.Read(buf, read, (int)(n - read));
                    if (r <= 0) throw new EndOfStreamException();
                    read += r;
                }
                return buf;
            }

            ulong BE(int bytes)
            {
                ulong v = 0;
                for (int i = 0; i < bytes; i++) v = (v << 8) | (uint)B();
                return v;
            }

            public object ReadValue()
            {
                int t = B();
                if (t <= 0x7f) return (long)t;
                if (t >= 0xe0) return (long)(sbyte)t;
                if ((t & 0xf0) == 0x80) return Map(t & 0x0f);
                if ((t & 0xf0) == 0x90) return Array(t & 0x0f);
                if ((t & 0xe0) == 0xa0) return Encoding.UTF8.GetString(Bytes(t & 0x1f));

                switch (t)
                {
                    case 0xc0: return null;
                    case 0xc2: return false;
                    case 0xc3: return true;
                    case 0xc4: return Bytes((long)BE(1));
                    case 0xc5: return Bytes((long)BE(2));
                    case 0xc6: return Bytes((long)BE(4));
                    case 0xc7: { long n = (long)BE(1); B(); return Bytes(n); }
                    case 0xc8: { long n = (long)BE(2); B(); return Bytes(n); }
                    case 0xc9: { long n = (long)BE(4); B(); return Bytes(n); }
                    case 0xca: Bytes(4); return 0.0;
                    case 0xcb: Bytes(8); return 0.0;
                    case 0xcc: return (long)BE(1);
                    case 0xcd: return (long)BE(2);
                    case 0xce: return (long)BE(4);
                    case 0xcf: return (long)BE(8);
                    case 0xd0: return (long)(sbyte)BE(1);
                    case 0xd1: return (long)(short)BE(2);
                    case 0xd2: return (long)(int)BE(4);
                    case 0xd3: return (long)BE(8);
                    case 0xd4: B(); return Bytes(1); // fixext (nvim handles: buffer/window/tabpage)
                    case 0xd5: B(); return Bytes(2);
                    case 0xd6: B(); return Bytes(4);
                    case 0xd7: B(); return Bytes(8);
                    case 0xd8: B(); return Bytes(16);
                    case 0xd9: return Encoding.UTF8.GetString(Bytes((long)BE(1)));
                    case 0xda: return Encoding.UTF8.GetString(Bytes((long)BE(2)));
                    case 0xdb: return Encoding.UTF8.GetString(Bytes((long)BE(4)));
                    case 0xdc: return Array((int)BE(2));
                    case 0xdd: return Array((int)BE(4));
                    case 0xde: return Map((int)BE(2));
                    case 0xdf: return Map((int)BE(4));
                    default: throw new IOException($"Unexpected msgpack byte 0x{t:x2}");
                }
            }

            List<object> Array(int n)
            {
                var list = new List<object>(n);
                for (int i = 0; i < n; i++) list.Add(ReadValue());
                return list;
            }

            Dictionary<string, object> Map(int n)
            {
                var map = new Dictionary<string, object>(n);
                for (int i = 0; i < n; i++)
                {
                    string key = Convert.ToString(ReadValue());
                    map[key ?? ""] = ReadValue();
                }
                return map;
            }
        }
    }
}
