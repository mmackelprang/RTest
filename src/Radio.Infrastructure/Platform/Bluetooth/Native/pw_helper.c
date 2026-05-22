/*
 * pw_helper.c — Thin C wrappers exposing PipeWire helpers that .NET cannot
 * P/Invoke directly. PipeWire 0.3 ships a number of useful APIs as either
 *   1. preprocessor macros (e.g., SPA pod builders), or
 *   2. `static inline` functions that expand the `spa_interface_call_res`
 *      vtable-dispatch macro (e.g., `pw_core_get_registry`,
 *      `pw_registry_add_listener`, `spa_dict_lookup`).
 *
 * Neither is reachable via DllImport because no real symbol is exported from
 * libpipewire-0.3.so. We materialise each one as an exported helper here.
 *
 * Currently exposed:
 *   - pw_helper_build_s16le_format_pod  (SPA pod builder, macro-only in headers)
 *   - pw_helper_spa_dict_lookup         (spa_dict_lookup, static-inline)
 *   - pw_helper_core_get_registry       (pw_core_get_registry, static-inline)
 *
 * Build:
 *   gcc -shared -fPIC -O2 -o libpw_helper.so pw_helper.c \
 *       $(pkg-config --cflags --libs libpipewire-0.3)
 *   sudo cp libpw_helper.so /usr/local/lib/
 *   sudo ldconfig
 *
 * The ldconfig step matters: the .NET runtime resolves `libpw_helper` via
 * the dynamic linker, which only sees the file once it's been picked up by
 * ldconfig (or `LD_LIBRARY_PATH`).
 */

#include <spa/param/audio/format-utils.h>
#include <spa/pod/builder.h>
#include <spa/utils/dict.h>
#include <pipewire/pipewire.h>

/**
 * Builds an SPA_FORMAT_AUDIO_RAW pod for S16_LE capture.
 *
 * @param buffer   Caller-allocated buffer (256 bytes is plenty).
 * @param buf_size Size of the buffer in bytes.
 * @param rate     Sample rate (e.g. 48000).
 * @param channels Number of channels (e.g. 2).
 * @return         Byte count written, or 0 on error.
 */
int pw_helper_build_s16le_format_pod(
    void *buffer, int buf_size, int rate, int channels)
{
    if (!buffer || buf_size < 128)
        return 0;

    struct spa_pod_builder b;
    spa_pod_builder_init(&b, buffer, (uint32_t)buf_size);

    struct spa_audio_info_raw info = SPA_AUDIO_INFO_RAW_INIT(
        .format   = SPA_AUDIO_FORMAT_S16_LE,
        .rate     = (uint32_t)rate,
        .channels = (uint32_t)channels
    );

    struct spa_pod *pod = spa_format_audio_raw_build(&b, SPA_PARAM_EnumFormat, &info);
    if (!pod)
        return 0;

    return (int)SPA_POD_SIZE(pod);
}

/**
 * Looks up a key in a spa_dict and returns the value (as a C string pointer).
 *
 * Used by PipeWireRegistryListener (Plan E) to read node.name from the
 * spa_dict* exposed to the pw_registry global() callback. P/Invoking
 * spa_dict_lookup directly is awkward because it is a static-inline helper
 * (not exported). This thin wrapper materialises it as a real symbol.
 *
 * @param dict  Pointer to spa_dict. Safe to pass NULL.
 * @param key   Null-terminated key to look up. Safe to pass NULL.
 * @return      Value string pointer (lifetime tied to the dict), or NULL if
 *              the dict is NULL, the key is NULL, or the key is missing.
 */
const char *pw_helper_spa_dict_lookup(const struct spa_dict *dict, const char *key)
{
    if (!dict || !key)
        return NULL;
    return spa_dict_lookup(dict, key);
}

/**
 * Wraps the static-inline `pw_core_get_registry` from <pipewire/core.h>.
 *
 * The PipeWire 0.3 ABI exposes `pw_core_get_registry` ONLY as a `static inline`
 * helper that expands the `spa_interface_call_res` vtable-dispatch macro. There
 * is no exported symbol named `pw_core_get_registry` in libpipewire-0.3.so, so
 * .NET P/Invoke cannot bind to it directly (it throws EntryPointNotFoundException).
 *
 * This thin wrapper compiles the inline helper into a real, exported symbol
 * (`pw_helper_core_get_registry`) that managed callers can DllImport via
 * `libpw_helper`. The semantics are identical: returns a `pw_registry*` or NULL.
 *
 * @param core            pw_core* returned by pw_context_connect. Safe to pass NULL.
 * @param version         Registry interface version (PW_VERSION_REGISTRY = 3).
 * @param user_data_size  Bytes of caller user-data to attach to the proxy
 *                        (0 for the common case).
 * @return                pw_registry* on success, NULL if `core` is NULL or
 *                        the vtable call failed.
 */
void *pw_helper_core_get_registry(void *core, uint32_t version, size_t user_data_size)
{
    if (!core)
        return NULL;
    return pw_core_get_registry((struct pw_core *)core, version, user_data_size);
}
