using System;
using System.IO;
using System.Text;

namespace BanglaHost.Core;

/// <summary>Drops a WordPress mu-plugin that rewrites <c>siteurl</c>/<c>home</c> to the current
/// Cloudflare quick-tunnel URL whenever the request Host is <c>*.trycloudflare.com</c>. Without
/// this, WP reads the DB-stored <c>https://&lt;site&gt;.test</c> for every asset/redirect, and
/// external visitors hit the tunnel URL only to be 301'd to a hostname that doesn't resolve on
/// the internet ("Server not found").
///
/// mu-plugins load before regular plugins/theme and can't be disabled from wp-admin — perfect for
/// a transient patch that's owned by the tooling. On <c>tunnel stop</c> we remove the drop-in so
/// the site behaves normally again.</summary>
public static class WpTunnelBridge
{
    private const string Filename = "banglahost-tunnel.php";

    private static bool IsWordPress(string root)
    {
        try { return File.Exists(Path.Combine(root, "wp-load.php")) || File.Exists(Path.Combine(root, "wp-config.php")); }
        catch { return false; }
    }

    public static void Install(string root, string tunnelUrl, Action<string>? info = null)
    {
        try
        {
            if (!IsWordPress(root)) return;
            var muDir = Path.Combine(root, "wp-content", "mu-plugins");
            Directory.CreateDirectory(muDir);
            var target = Path.Combine(muDir, Filename);
            File.WriteAllText(target, PluginPhp(tunnelUrl), new UTF8Encoding(false));
            info?.Invoke($"wp tunnel bridge installed: wp-content/mu-plugins/{Filename}");
        }
        catch (Exception ex) { info?.Invoke("wp tunnel bridge skipped: " + ex.Message); }
    }

    public static void Uninstall(string root)
    {
        try
        {
            var target = Path.Combine(root, "wp-content", "mu-plugins", Filename);
            if (File.Exists(target)) File.Delete(target);
        }
        catch { }
    }

    private static string PluginPhp(string tunnelUrl)
    {
        var escaped = tunnelUrl.Replace("'", "\\'");
        return @"<?php
/**
 * BanglaHost tunnel bridge (auto-generated).
 * Rewrites siteurl/home and the request scheme to the live Cloudflare quick tunnel
 * when the incoming Host is *.trycloudflare.com. Removed automatically when the
 * tunnel is stopped from BanglaHost.
 */
if (!defined('ABSPATH')) return;

$__bh_tunnel = '" + escaped + @"';
$__bh_parts  = parse_url($__bh_tunnel);
$__bh_public = isset($__bh_parts['host']) ? strtolower($__bh_parts['host']) : '';

// cloudflared rewrites the outgoing Host to <site>.test (via --http-host-header) so HTTP_HOST
// looks local. Detect a tunneled request by Cloudflare's own headers (CF-Ray is only present on
// requests that traversed Cloudflare's edge).
$__bh_on = isset($_SERVER['HTTP_CF_RAY'])
        || isset($_SERVER['HTTP_CF_CONNECTING_IP'])
        || (isset($_SERVER['HTTP_X_FORWARDED_HOST']) && stripos($_SERVER['HTTP_X_FORWARDED_HOST'], '.trycloudflare.com') !== false);

if ($__bh_on && $__bh_public !== '') {
    // cloudflared terminates TLS and forwards HTTP to our local origin; make WP treat this as HTTPS.
    $_SERVER['HTTPS']       = 'on';
    $_SERVER['SERVER_PORT'] = 443;
    // Fix HTTP_HOST so any code reading it (WP included) sees the public host.
    $_SERVER['HTTP_HOST']   = $__bh_public;

    $__bh_replace = function ($v) use ($__bh_tunnel) { return $__bh_tunnel; };
    add_filter('pre_option_siteurl', $__bh_replace);
    add_filter('pre_option_home',    $__bh_replace);

    // Some plugins/themes hit the DB directly for siteurl/home — normalise their output too.
    $__bh_rewrite = function ($url) use ($__bh_tunnel) {
        if (!is_string($url) || $url === '') return $url;
        $parts = wp_parse_url($__bh_tunnel);
        if (!$parts || empty($parts['host'])) return $url;
        return preg_replace('#^https?://[^/]+#i', $parts['scheme'] . '://' . $parts['host'], $url);
    };
    foreach (array('site_url','home_url','admin_url','network_site_url','network_home_url','plugins_url','content_url','includes_url','wp_get_attachment_url') as $__bh_hook) {
        add_filter($__bh_hook, $__bh_rewrite, 20);
    }

    // Cookie domain must match the public host or admin login just loops.
    if (!defined('COOKIE_DOMAIN')) define('COOKIE_DOMAIN', $__bh_public);
}
";
    }
}
