package io.github.yany1944.tapmirror;

import android.content.Context;
import android.net.ConnectivityManager;
import android.net.LinkAddress;
import android.net.LinkProperties;
import android.net.Network;
import android.net.NetworkCapabilities;
import android.os.Build;
import android.util.Log;

import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.Inet4Address;
import java.net.InetAddress;
import java.net.InetSocketAddress;
import java.net.SocketTimeoutException;
import java.net.URLDecoder;
import java.net.URLEncoder;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * The phone side of the UDP protocol (see docs/protocol.md).
 * Every message is one datagram: "TAPMIRROR/1 TYPE key=value key=value", values URL-encoded.
 */
final class Net {
    static final String TAG = "TapMirror";
    static final int PC_PORT = 27199;
    private static final String MAGIC = "TAPMIRROR/1";

    private Net() {}

    static final class Msg {
        final String type;
        final Map<String, String> kv;
        InetSocketAddress from;

        Msg(String type, Map<String, String> kv) {
            this.type = type;
            this.kv = kv;
        }

        String get(String k) {
            String v = kv.get(k);
            return v == null ? "" : v;
        }

        static byte[] build(String type, String... pairs) {
            StringBuilder sb = new StringBuilder(MAGIC).append(' ').append(type);
            for (int i = 0; i + 1 < pairs.length; i += 2) {
                sb.append(' ').append(pairs[i]).append('=')
                        .append(URLEncoder.encode(pairs[i + 1], StandardCharsets.UTF_8));
            }
            return sb.toString().getBytes(StandardCharsets.UTF_8);
        }

        static Msg parse(DatagramPacket p) {
            String s = new String(p.getData(), p.getOffset(), p.getLength(), StandardCharsets.UTF_8);
            String[] parts = s.trim().split(" ");
            if (parts.length < 2 || !MAGIC.equals(parts[0])) return null;
            Map<String, String> kv = new LinkedHashMap<>();
            for (int i = 2; i < parts.length; i++) {
                int eq = parts[i].indexOf('=');
                if (eq <= 0) continue;
                try {
                    kv.put(parts[i].substring(0, eq), URLDecoder.decode(parts[i].substring(eq + 1), StandardCharsets.UTF_8));
                } catch (IllegalArgumentException ignored) {
                }
            }
            Msg m = new Msg(parts[1], kv);
            m.from = (InetSocketAddress) p.getSocketAddress();
            return m;
        }
    }

    static final class Pc {
        final String id;
        final String name;
        final InetSocketAddress addr;

        Pc(String id, String name, InetSocketAddress addr) {
            this.id = id;
            this.name = name;
            this.addr = addr;
        }
    }

    /** The current Wi-Fi network (not a VPN on top of it), or null. */
    static Network wifi(Context c) {
        ConnectivityManager cm = c.getSystemService(ConnectivityManager.class);
        for (Network n : cm.getAllNetworks()) {
            NetworkCapabilities nc = cm.getNetworkCapabilities(n);
            // a VPN reports the transport of the network under it, so it looks like Wi-Fi too
            if (nc != null && nc.hasTransport(NetworkCapabilities.TRANSPORT_WIFI)
                    && nc.hasCapability(NetworkCapabilities.NET_CAPABILITY_NOT_VPN)) return n;
        }
        return null;
    }

    /**
     * A UDP socket on the Wi-Fi network. A VPN that apps may bypass would otherwise take the
     * traffic; one that apps may not bypass refuses the binding, and then the socket goes through
     * the VPN (which usually passes local traffic on, but can't return answers to broadcasts).
     */
    static DatagramSocket openSocket(Network wifi) throws Exception {
        DatagramSocket s = new DatagramSocket();
        try {
            wifi.bindSocket(s);
        } catch (Exception e) {
            Log.i(TAG, "can't bind to Wi-Fi (a VPN that can't be bypassed?), using the default route: " + e);
        }
        s.setBroadcast(true);
        return s;
    }

    static void send(DatagramSocket s, InetSocketAddress to, byte[] data) {
        try {
            s.send(new DatagramPacket(data, data.length, to));
        } catch (Exception e) {
            Log.w(TAG, "send to " + to + " failed: " + e);
        }
    }

    /**
     * Finds PCs running TapMirror. Asks the last known PC and the broadcast addresses first;
     * if nobody answers in half a second, asks every address of the local /24 one by one
     * (answers to broadcasts don't come back through some VPNs, answers to unicast do).
     */
    static List<Pc> discover(Context c, DatagramSocket s, Network wifi, String phoneId, String lastIp) throws Exception {
        List<InetSocketAddress> first = new ArrayList<>();
        List<InetSocketAddress> sweep = new ArrayList<>();
        if (lastIp != null) first.add(new InetSocketAddress(InetAddress.getByName(lastIp), PC_PORT));
        LinkProperties lp = c.getSystemService(ConnectivityManager.class).getLinkProperties(wifi);
        if (lp != null) {
            for (LinkAddress la : lp.getLinkAddresses()) {
                if (!(la.getAddress() instanceof Inet4Address)) continue;
                first.add(new InetSocketAddress(broadcast(la), PC_PORT));
                sweep.addAll(neighbours(la));
            }
        }
        first.add(new InetSocketAddress(InetAddress.getByName("255.255.255.255"), PC_PORT));
        byte[] hello = Msg.build("HELLO", "id", phoneId, "name", Build.MODEL);

        Map<String, Pc> found = new LinkedHashMap<>();
        byte[] buf = new byte[1500];
        long begun = System.currentTimeMillis();
        long end = begun + 2500;
        boolean swept = false, resent = false;
        for (InetSocketAddress a : first) send(s, a, hello);
        s.setSoTimeout(100);
        while (System.currentTimeMillis() < end) {
            long t = System.currentTimeMillis() - begun;
            if (found.isEmpty() && !swept && t > 500) {
                Log.i(TAG, "no answer to broadcast, asking " + sweep.size() + " addresses");
                for (InetSocketAddress a : sweep) send(s, a, hello);
                swept = true;
            }
            if (found.isEmpty() && !resent && t > 1200) {
                for (InetSocketAddress a : first) send(s, a, hello);
                resent = true;
            }
            try {
                DatagramPacket p = new DatagramPacket(buf, buf.length);
                s.receive(p);
                Msg m = Msg.parse(p);
                if (m != null && "HI".equals(m.type) && !m.get("pc").isEmpty()) {
                    Log.i(TAG, "PC " + m.get("name") + " at " + m.from);
                    found.put(m.get("pc"), new Pc(m.get("pc"), m.get("name"), m.from));
                    // a PC usually answers within a few ms; give others a short moment too
                    end = Math.min(end, System.currentTimeMillis() + 400);
                }
            } catch (SocketTimeoutException ignored) {
            }
        }
        return new ArrayList<>(found.values());
    }

    private static int toInt(InetAddress a) {
        byte[] b = a.getAddress();
        return ((b[0] & 0xff) << 24) | ((b[1] & 0xff) << 16) | ((b[2] & 0xff) << 8) | (b[3] & 0xff);
    }

    private static InetAddress fromInt(int v) throws Exception {
        return InetAddress.getByAddress(new byte[]{(byte) (v >>> 24), (byte) (v >>> 16), (byte) (v >>> 8), (byte) v});
    }

    private static InetAddress broadcast(LinkAddress la) throws Exception {
        int prefix = la.getPrefixLength();
        int mask = prefix == 0 ? 0 : -1 << (32 - prefix);
        return fromInt(toInt(la.getAddress()) | ~mask);
    }

    /** Every other host address of our subnet, limited to the /24 around us. */
    private static List<InetSocketAddress> neighbours(LinkAddress la) throws Exception {
        int prefix = Math.max(la.getPrefixLength(), 24);
        int self = toInt(la.getAddress());
        int net = self & (-1 << (32 - prefix));
        int count = (1 << (32 - prefix)) - 2;
        List<InetSocketAddress> out = new ArrayList<>();
        for (int h = 1; h <= count; h++) {
            if (net + h != self) out.add(new InetSocketAddress(fromInt(net + h), PC_PORT));
        }
        return out;
    }
}
