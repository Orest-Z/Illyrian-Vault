/* =======================================================
 * Copyright (c) 2026 Orest Zogju. All Rights Reserved.
 * Illyrian Vault - Local & Encrypted Password Manager
 * Unauthorized copying of this file is strictly prohibited.
 * ======================================================= */
using System.Windows;
using System.Windows.Controls;
using IllyrianVault.Models;

namespace IllyrianVault.Helpers;

public class PayloadViewSelector : DataTemplateSelector
{
    public DataTemplate? LoginTemplate    { get; set; }
    public DataTemplate? NoteTemplate     { get; set; }
    public DataTemplate? CardTemplate     { get; set; }
    public DataTemplate? IdentityTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) =>
        item switch {
            LoginPayload    => LoginTemplate,
            NotePayload     => NoteTemplate,
            CardPayload     => CardTemplate,
            IdentityPayload => IdentityTemplate,
            _               => null,
        };
}

public class PayloadEditSelector : DataTemplateSelector
{
    public DataTemplate? LoginTemplate    { get; set; }
    public DataTemplate? NoteTemplate     { get; set; }
    public DataTemplate? CardTemplate     { get; set; }
    public DataTemplate? IdentityTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) =>
        item switch {
            LoginPayload    => LoginTemplate,
            NotePayload     => NoteTemplate,
            CardPayload     => CardTemplate,
            IdentityPayload => IdentityTemplate,
            _               => null,
        };
}
