/**
 * @name FlipClockBridge
 * @description Lets the Flip Clock app show its Discord bubble straight from Discord, instead of relying on Windows notifications. Shares only counts and times (never message text) through a small file on this PC.
 * @version 1.0.0
 * @author Flip Clock
 */

/*
 * What this writes, to %APPDATA%\FlipClock\discord-bridge.json (and BetterDiscord's own
 * plugins\FlipClockBridge.config.json as a backup), times in milliseconds since 1970:
 *
 *   running           false once the plugin is turned off
 *   heartbeat         rewritten every few seconds; the clock ignores the file if this goes stale
 *   mentions          Discord's red badge number: server @mentions + unread DMs + friend requests
 *   mentionsRaised    when that number last went up
 *   notifications     desktop notifications Discord showed since you last focused Discord
 *   lastNotification  when Discord last showed one
 *   lastFocus         when you last focused the Discord window
 */

const NAME = "FlipClockBridge";
const CHECK_MS = 2000;        // recount even if no store says anything changed
const HEARTBEAT_MS = 5000;    // rewrite the file at least this often so the clock knows Discord is alive
const STARTUP_MS = 20000;     // while Discord is still loading, unread counts climb from 0; don't treat that as new

module.exports = class FlipClockBridge {
    constructor(meta) {
        this.meta = meta;
    }

    start() {
        const prev = this.load();
        const now = Date.now();
        this.startedAt = now;
        this.prevMentions = prev.mentions || 0;
        this.state = {
            version: 1,
            running: true,
            heartbeat: now,
            mentions: prev.mentions || 0,
            mentionsRaised: prev.mentionsRaised || 0,
            notifications: prev.notifications || 0,
            lastNotification: prev.lastNotification || 0,
            lastFocus: prev.lastFocus || 0,
        };
        this.lastKey = "";
        this.lastWriteAt = 0;

        this.setupFile();
        this.findStores();

        this.onStoreChange = () => this.scheduleUpdate();
        for (const store of this.listenStores) {
            try { store.addChangeListener(this.onStoreChange); } catch { /* not a flux store */ }
        }

        this.onFocus = () => this.focused();
        window.addEventListener("focus", this.onFocus);
        if (document.hasFocus()) this.focused(false);

        this.patchNotifications();

        this.timer = setInterval(() => this.update(), CHECK_MS);
        this.update(true);
    }

    stop() {
        clearInterval(this.timer);
        clearTimeout(this.pending);
        window.removeEventListener("focus", this.onFocus);
        for (const store of this.listenStores || []) {
            try { store.removeChangeListener(this.onStoreChange); } catch { /* ignore */ }
        }
        this.unpatchNotifications();
        if (this.state) {
            this.state.running = false;
            this.write(true);
        }
    }

    // ------------------------------------------------------------------ Discord's own unread counts

    getStore(name) {
        const W = BdApi.Webpack;
        try {
            const store = W.getStore?.(name);
            if (store) return store;
        } catch { /* fall through */ }
        try {
            return W.getModule((m) => m?.getName?.() === name || m?.constructor?.displayName === name) || null;
        } catch {
            return null;
        }
    }

    findStores() {
        this.stores = {};
        for (const name of [
            "ReadStateStore", "GuildReadStateStore", "GuildStore", "ChannelStore",
            "PrivateChannelSortStore", "UserGuildSettingsStore", "RelationshipStore",
        ]) {
            this.stores[name] = this.getStore(name);
        }
        const s = this.stores;
        this.listenStores = [s.ReadStateStore, s.GuildReadStateStore, s.RelationshipStore].filter(Boolean);
        if (!s.ReadStateStore || !s.GuildReadStateStore) {
            console.warn(`[${NAME}] Couldn't find Discord's unread stores; only desktop notifications will reach the clock.`);
        }
    }

    privateChannelIds() {
        const { PrivateChannelSortStore, ChannelStore } = this.stores;
        try {
            const ids = PrivateChannelSortStore?.getPrivateChannelIds?.();
            if (Array.isArray(ids)) return ids;
        } catch { /* try the next way */ }
        try {
            const list = ChannelStore?.getSortedPrivateChannels?.();
            if (Array.isArray(list)) return list.map((c) => c.id);
        } catch { /* try the next way */ }
        try {
            const map = ChannelStore?.getMutablePrivateChannels?.();
            if (map) return Object.keys(map);
        } catch { /* give up */ }
        return [];
    }

    /** The same number Discord puts on its taskbar icon. */
    countMentions() {
        const { ReadStateStore, GuildReadStateStore, GuildStore, UserGuildSettingsStore, RelationshipStore } = this.stores;
        let total = 0;

        // @mentions in servers (Discord already applies your mute / suppress settings to these).
        if (GuildReadStateStore?.getMentionCount && GuildStore?.getGuilds) {
            for (const id of Object.keys(GuildStore.getGuilds() || {})) {
                total += GuildReadStateStore.getMentionCount(id) || 0;
            }
        }

        // Unread messages in DMs and group DMs, skipping muted ones.
        if (ReadStateStore?.getMentionCount) {
            for (const id of this.privateChannelIds()) {
                try {
                    if (UserGuildSettingsStore?.isChannelMuted?.(null, id)) continue;
                } catch { /* count it */ }
                total += ReadStateStore.getMentionCount(id) || 0;
            }
        }

        // Incoming friend requests.
        try { total += RelationshipStore?.getPendingCount?.() || 0; } catch { /* ignore */ }

        return total;
    }

    scheduleUpdate() {
        // Stores change on every message; batch them.
        if (this.pending) return;
        this.pending = setTimeout(() => {
            this.pending = null;
            this.update();
        }, 250);
    }

    update(force = false) {
        const now = Date.now();
        try {
            const m = this.countMentions();
            const loading = now - this.startedAt < STARTUP_MS && m <= this.prevMentions;
            if (m > this.state.mentions && !loading) this.state.mentionsRaised = now;
            this.state.mentions = m;
        } catch (e) {
            console.warn(`[${NAME}] Couldn't count unread messages`, e);
        }
        this.write(force);
    }

    // ------------------------------------------------------------------ desktop notifications

    patchNotifications() {
        const Original = window.Notification;
        if (typeof Original !== "function" || Original.__flipClock) return;
        const plugin = this;

        function FlipClockNotification(title, options) {
            const n = new Original(title, options);
            try { plugin.notified(); } catch { /* never break Discord's notification */ }
            return n;
        }
        FlipClockNotification.prototype = Original.prototype;
        Object.defineProperty(FlipClockNotification, "permission", { get: () => Original.permission });
        Object.defineProperty(FlipClockNotification, "maxActions", { get: () => Original.maxActions });
        FlipClockNotification.requestPermission = (...args) => Original.requestPermission(...args);
        FlipClockNotification.__flipClock = true;

        this.originalNotification = Original;
        window.Notification = FlipClockNotification;
    }

    unpatchNotifications() {
        if (this.originalNotification && window.Notification?.__flipClock) {
            window.Notification = this.originalNotification;
        }
        this.originalNotification = null;
    }

    notified() {
        this.state.notifications++;
        this.state.lastNotification = Date.now();
        this.write(true);
    }

    focused(write = true) {
        // Looking at Discord counts as having seen its notifications. Unread @mentions/DMs still count until read.
        this.state.notifications = 0;
        this.state.lastFocus = Date.now();
        if (write) this.write(true);
    }

    // ------------------------------------------------------------------ the file the clock reads

    setupFile() {
        this.fs = null;
        this.file = null;
        try {
            const fs = require("fs");
            const path = require("path");
            const env = typeof process !== "undefined" && process.env ? process.env : {};
            const appData = env.APPDATA || path.resolve(BdApi.Plugins.folder, "..", "..");
            const dir = path.join(appData, "FlipClock");
            try { fs.mkdirSync(dir, { recursive: true }); } catch { /* probably exists */ }
            this.fs = fs;
            this.file = path.join(dir, "discord-bridge.json");
        } catch (e) {
            console.warn(`[${NAME}] No file access; using BetterDiscord's plugin data file only`, e);
        }
    }

    load() {
        try { return BdApi.Data.load(NAME, "state") || {}; } catch { return {}; }
    }

    write(force) {
        const now = Date.now();
        const key = JSON.stringify({ ...this.state, heartbeat: 0 });
        if (!force && key === this.lastKey && now - this.lastWriteAt < HEARTBEAT_MS) return;
        this.lastKey = key;
        this.lastWriteAt = now;
        this.state.heartbeat = now;
        const json = JSON.stringify(this.state);

        if (this.fs && this.file) {
            try {
                const tmp = this.file + ".tmp";
                this.fs.writeFileSync(tmp, json);
                try {
                    this.fs.renameSync(tmp, this.file);
                } catch {
                    this.fs.writeFileSync(this.file, json);
                }
            } catch (e) {
                console.warn(`[${NAME}] Couldn't write ${this.file}`, e);
            }
        }
        // Backup channel the clock also reads: BetterDiscord saves this as plugins\FlipClockBridge.config.json.
        try { BdApi.Data.save(NAME, "state", this.state); } catch { /* ignore */ }
    }
};
