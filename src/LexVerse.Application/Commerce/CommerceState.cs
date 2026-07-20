using LexVerse.Application.Account;
using LexVerse.Core.Product;

namespace LexVerse.Application.Commerce;

public enum CommerceStatus
{
    Unconfigured,
    SignedOut,
    Loading,
    Ready,
    CheckoutOpened,
    Error
}

public sealed record CommerceState(
    CommerceStatus Status,
    AccountSession? Account = null,
    ProductCatalog? Catalog = null);
