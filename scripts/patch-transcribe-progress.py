from __future__ import annotations

import pathlib
import sys


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected exactly one anchor, found {count}")
    return text.replace(old, new, 1)


def main() -> int:
    if len(sys.argv) != 2:
        print("usage: patch-transcribe-progress.py <transcribe.cpp source root>", file=sys.stderr)
        return 2

    root = pathlib.Path(sys.argv[1])
    path = root / "src" / "arch" / "moss" / "model.cpp"
    text = path.read_text(encoding="utf-8")

    text = replace_once(
        text,
        '#include <cstdint>\n#include <cstdio>',
        '#include <cstdint>\n#include <cstdio>\n#include <cstdlib>',
        "include cstdlib",
    )

    text = replace_once(
        text,
        """namespace {

struct OpProf {""",
        r"""namespace {

// Speak2Text private build progress protocol.
// stderr only, so --batch-jsonl stdout remains valid JSONL.
void s2t_progress(const char * phase,
                  int          done,
                  int          total,
                  int64_t      covered_ms,
                  int64_t      audio_ms) {
    std::fprintf(stderr, "S2T_PROGRESS|MOSS|%s|%d|%d|%lld|%lld\n", phase, done, total,
                 static_cast<long long>(covered_ms), static_cast<long long>(audio_ms));
    std::fflush(stderr);
}

// MOSS emits numeric [seconds] markers inline with [Sxx] speaker tags.
// Return the latest complete numeric marker from the partial decoded text.
int64_t s2t_last_timestamp_ms(const std::string & text) {
    int64_t last_ms = -1;
    size_t  pos     = 0;
    while ((pos = text.find('[', pos)) != std::string::npos) {
        const size_t end = text.find(']', pos + 1);
        if (end == std::string::npos) {
            break;
        }
        const std::string value = text.substr(pos + 1, end - pos - 1);
        if (!value.empty() && value[0] != 'S' && value[0] != 's') {
            char *       parse_end = nullptr;
            const double seconds   = std::strtod(value.c_str(), &parse_end);
            if (parse_end != value.c_str() && parse_end != nullptr && *parse_end == '\0' &&
                std::isfinite(seconds) && seconds >= 0.0) {
                last_ms = static_cast<int64_t>(seconds * 1000.0 + 0.5);
            }
        }
        pos = end + 1;
    }
    return last_ms;
}

struct OpProf {""",
        "progress helpers",
    )

    text = replace_once(
        text,
        """    std::vector<float> concat_trim;  // [d_model, T_trim] column-major (d innermost)
    int                T_trim_total = 0;

    for (int start = 0; start < n_samples; start += n_chunk) {""",
        """    std::vector<float> concat_trim;  // [d_model, T_trim] column-major (d innermost)
    int                T_trim_total = 0;

    const int     s2t_chunk_total = (n_samples + n_chunk - 1) / n_chunk;
    const int64_t s2t_audio_ms =
        static_cast<int64_t>(n_samples) * 1000 / static_cast<int64_t>(hp.fe_sample_rate);
    int s2t_chunk_done = 0;
    s2t_progress("ENCODE", 0, s2t_chunk_total, 0, s2t_audio_ms);

    for (int start = 0; start < n_samples; start += n_chunk) {""",
        "encoder start",
    )

    text = replace_once(
        text,
        """        T_trim_total += keep_cols;
    }

    if (T_trim_total <= 0 || T_trim_total % merge != 0) {""",
        """        T_trim_total += keep_cols;

        ++s2t_chunk_done;
        const int64_t covered_ms =
            static_cast<int64_t>(std::min(n_samples, start + real_len)) * 1000 /
            static_cast<int64_t>(hp.fe_sample_rate);
        s2t_progress("ENCODE", s2t_chunk_done, s2t_chunk_total, covered_ms, s2t_audio_ms);
    }

    if (T_trim_total <= 0 || T_trim_total % merge != 0) {""",
        "encoder chunk progress",
    )

    text = replace_once(
        text,
        """    ggml_backend_tensor_get(ab.out, enc_out.data(), 0, enc_out.size() * sizeof(float));
    return TRANSCRIBE_OK;
}

// Build audio_dense""",
        """    ggml_backend_tensor_get(ab.out, enc_out.data(), 0, enc_out.size() * sizeof(float));
    s2t_progress("ADAPTOR", 1, 1, s2t_audio_ms, s2t_audio_ms);
    return TRANSCRIBE_OK;
}

// Build audio_dense""",
        "adaptor progress",
    )

    text = replace_once(
        text,
        """    log_msg(TRANSCRIBE_LOG_LEVEL_DEBUG, "moss prefill: %d tokens in %d chunks of %d", T_prompt, n_chunks, chunk_size);

    for (int c = 0; c < n_chunks; ++c) {""",
        """    log_msg(TRANSCRIBE_LOG_LEVEL_DEBUG, "moss prefill: %d tokens in %d chunks of %d", T_prompt, n_chunks, chunk_size);
    s2t_progress("PREFILL", 0, n_chunks, 0, 0);

    for (int c = 0; c < n_chunks; ++c) {""",
        "prefill start",
    )

    text = replace_once(
        text,
        """        cc->kv_cache.n    = max_n_kv;
        cc->kv_cache.head = max_n_kv;

        if (last) {""",
        """        cc->kv_cache.n    = max_n_kv;
        cc->kv_cache.head = max_n_kv;
        s2t_progress("PREFILL", c + 1, n_chunks, 0, 0);

        if (last) {""",
        "prefill chunk progress",
    )

    text = replace_once(
        text,
        """    transcribe::debug::init();
    const bool dumps_on = transcribe::debug::enabled();

    // The callback persists""",
        """    transcribe::debug::init();
    const bool dumps_on = transcribe::debug::enabled();
    const int64_t s2t_audio_ms =
        static_cast<int64_t>(n_samples) * 1000 / static_cast<int64_t>(cm->hparams.fe_sample_rate);
    s2t_progress("START", 0, 1, 0, s2t_audio_ms);

    // The callback persists""",
        "run start",
    )

    text = replace_once(
        text,
        """        ggml_backend_tensor_get(pb.out, logits.data(), 0, logits.size() * sizeof(float));
    }

    std::vector<int32_t> generated_ids;""",
        """        ggml_backend_tensor_get(pb.out, logits.data(), 0, logits.size() * sizeof(float));
        s2t_progress("PREFILL", 1, 1, 0, s2t_audio_ms);
    }

    std::vector<int32_t> generated_ids;""",
        "single prefill progress",
    )

    text = replace_once(
        text,
        """    if (perf_debug) {
        per_step_us.reserve(512);
    }

    while (next_tok != eos_id""",
        """    if (perf_debug) {
        per_step_us.reserve(512);
    }

    s2t_progress("DECODE", 0, gen_budget, 0, s2t_audio_ms);

    while (next_tok != eos_id""",
        "decode start",
    )

    text = replace_once(
        text,
        """        cc->kv_cache.n    = cur_past + 1;
        cc->kv_cache.head = cur_past + 1;
        if (cc->poll_abort()) {""",
        """        cc->kv_cache.n    = cur_past + 1;
        cc->kv_cache.head = cur_past + 1;

        const int s2t_generated = static_cast<int>(generated_ids.size());
        if (next_tok == eos_id || (s2t_generated % 8) == 0) {
            const std::string partial_text = cm->tok.decode(generated_ids.data(), s2t_generated);
            int64_t covered_ms = s2t_last_timestamp_ms(partial_text);
            if (covered_ms < 0) {
                covered_ms = 0;
            }
            covered_ms = std::min(covered_ms, s2t_audio_ms);
            s2t_progress("DECODE", s2t_generated, gen_budget, covered_ms, s2t_audio_ms);
        }

        if (cc->poll_abort()) {""",
        "decode live progress",
    )

    text = replace_once(
        text,
        """    std::string raw_text = cm->tok.decode(generated_ids.data(), static_cast<int>(generated_ids.size()));

    cc->t_decode_us = ggml_time_us() - t_dec_start;""",
        """    std::string raw_text = cm->tok.decode(generated_ids.data(), static_cast<int>(generated_ids.size()));
    s2t_progress("DECODE", gen_budget, gen_budget, s2t_audio_ms, s2t_audio_ms);

    cc->t_decode_us = ggml_time_us() - t_dec_start;""",
        "decode final progress",
    )

    text = replace_once(
        text,
        """    cc->has_result = true;

    return cc->was_truncated ? TRANSCRIBE_ERR_OUTPUT_TRUNCATED : TRANSCRIBE_OK;""",
        """    cc->has_result = true;
    s2t_progress("DONE", 1, 1, s2t_audio_ms, s2t_audio_ms);

    return cc->was_truncated ? TRANSCRIBE_ERR_OUTPUT_TRUNCATED : TRANSCRIBE_OK;""",
        "run done",
    )

    path.write_text(text, encoding="utf-8")
    print(f"Patched MOSS progress protocol in {path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
