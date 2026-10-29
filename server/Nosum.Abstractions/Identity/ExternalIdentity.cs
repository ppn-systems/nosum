// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nosum.Contracts.Account;

namespace Nosum.Abstractions.Identity;

/// <summary>
/// An identity that a Google or Microsoft ID token proved (docs/adr/ADR-069). The <see cref="Subject"/> is the identity key; the e-mail is for display
/// and is never used to find an account.
/// </summary>
/// <param name="Provider">The provider that issued the token.</param>
/// <param name="Subject">The provider's stable subject: <c>sub</c> for Google, <c>oid</c> (else <c>sub</c>) for Microsoft.</param>
/// <param name="Email">The e-mail the provider reported, or null when it sent none.</param>
public readonly record struct ExternalIdentity(ExternalProvider Provider, string Subject, string? Email);
