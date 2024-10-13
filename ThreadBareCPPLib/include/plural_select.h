#ifndef PLURALSELECT_H
#define PLURALSELECT_H
#include <bn_fixed.h>
#include <bn_math.h>

/**
 * @brief Calculates the hash of the given const char *.
 * @param ptr Start position of the const char *.
 * @param size Size in bytes of the const char *.
 * todo move this into some other util file (maybe with some polish could be an MR to butano?)
 */
[[nodiscard]] constexpr unsigned operator "" _h (const char* ptr, size_t size)
{
    // FNV-1a:
    constexpr unsigned basis = 0x811C9DC5;
    constexpr unsigned prime = 0x01000193;

    unsigned result = basis;

    if(ptr && size > 0)
    {
        int word_size = size / 4;
        int words = word_size;
        {
        auto u8_ptr = ptr;

        while(words)
        {
            unsigned value = unsigned(u8_ptr[0]) |
                    unsigned(u8_ptr[1]) << 8 |
                    unsigned(u8_ptr[2]) << 16 |
                    unsigned(u8_ptr[3]) << 24;

            result *= prime;
            result ^= value;
            u8_ptr += 4;
            --words;
        }

        ptr = u8_ptr;
        }

        size -= word_size * 4;

        if(size)
        {
            auto u8_ptr = ptr;
            unsigned value = *u8_ptr;
            ++u8_ptr;
            --size;

            while(size)
            {
                value = (value << 8) + *u8_ptr;
                ++u8_ptr;
                --size;
            }

            result *= prime;
            result ^= value;
        }
    }

    return result;
}
enum class PluralCase : int { Zero, One, Two, Few, Many, Other };


// todo: when adding localization support, also take in the locale code and use that to swapout the logic
template <typename T>
constexpr auto GetCardinalPluralCase(T n) -> PluralCase {
    if(n == 1) return PluralCase::One;
    return PluralCase::Other;
}

template <typename T>
constexpr auto GetOrdinalPluralCase(T inputN) -> PluralCase {
    int n = bn::abs(inputN);
    if ((((n % 10) == 1) && !(((n % 100) == 11))))
    {
        return PluralCase::One;
    }

    if ((((n % 10) == 2) && !(((n % 100) == 12))))
    {
        return PluralCase::Two;
    }

    if ((((n % 10) == 3) && !(((n % 100) == 13))))
    {
        return PluralCase::Few;
    }

    return PluralCase::Other;  
}

#endif