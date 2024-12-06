<?xml version="1.0" encoding="utf-8"?>

<xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:msxsl="urn:schemas-microsoft-com:xslt"
                exclude-result-prefixes="msxsl"
                xmlns:wix="http://wixtoolset.org/schemas/v4/wxs">

  <xsl:output method="xml" indent="yes" />

  <xsl:template match="@* | node()">
    <xsl:copy>
      <xsl:apply-templates select="@* | node()" />
    </xsl:copy>
  </xsl:template>

  <xsl:key name="XmlToRemove"
           match="wix:Component[contains(wix:File/@Source, '.xml')]"
           use="@Id" />
  
  <xsl:key name="PdbToRemove"
           match="wix:Component[contains(wix:File/@Source, '.pdb')]"
           use="@Id" />

  <xsl:template match="wix:File[@Source='$(var.TestsAcceptanceDir)\appsettings.test.json']/@Id">
    <xsl:attribute name="{name()}">
      <xsl:value-of select="'TestAcceptanceConfig'" />
    </xsl:attribute>
  </xsl:template>

  <xsl:template match="wix:File[@Source='$(var.TestsAcceptanceDir)\Tests.Acceptance.dll']/@Id">
    <xsl:attribute name="{name()}">
      <xsl:value-of select="'TestAcceptanceDll'" />
    </xsl:attribute>
  </xsl:template>

  <xsl:template match="*[self::wix:Component or self::wix:ComponentRef]
                        [key('XmlToRemove', @Id)]" />

  <xsl:template match="*[self::wix:Component or self::wix:ComponentRef]
                        [key('PdbToRemove', @Id)]" />

</xsl:stylesheet>