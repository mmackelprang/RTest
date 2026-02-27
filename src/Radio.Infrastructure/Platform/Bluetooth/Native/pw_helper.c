/*
 * pw_helper.c — Minimal helper for building SPA format pods from C#.
 *
 * PipeWire's SPA pod builder macros are C-preprocessor-only and cannot
 * be called via P/Invoke. This tiny shared library exposes a single
 * function that builds an SPA_FORMAT_AUDIO_RAW pod (S16_LE, given rate
 * and channels) into a caller-supplied buffer.
 *
 * Build:
 *   gcc -shared -fPIC -o libpw_helper.so pw_helper.c \
 *       $(pkg-config --cflags --libs libpipewire-0.3)
 */

#include <spa/param/audio/format-utils.h>
#include <spa/pod/builder.h>

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
