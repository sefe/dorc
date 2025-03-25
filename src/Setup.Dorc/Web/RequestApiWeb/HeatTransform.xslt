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

  <xsl:template match="*[self::wix:Component or self::wix:ComponentRef]
                        [key('XmlToRemove', @Id)]" />

  <xsl:key name="PdbToRemove"
           match="wix:Component[contains(wix:File/@Source, '.pdb')]"
           use="@Id" />

  <xsl:key name="CsToRemove"
           match="wix:Component[substring(wix:File/@Source, string-length(wix:File/@Source) - string-length('.cs') +1)='.cs']"
           use="@Id" />

  <xsl:template match="wix:File[@Source='$(var.DorcApiWebDir)\appsettings.json']/@Id">
    <xsl:attribute name="{name()}">
      <xsl:value-of select="'DeployWebApiWebConfig'" />
    </xsl:attribute>
  </xsl:template>

  <xsl:template match="wix:File[@Source='$(var.DorcApiWebDir)\Dorc.Api.dll']/@Id">
    <xsl:attribute name="{name()}">
      <xsl:value-of select="'DeployApiWebDll'" />
    </xsl:attribute>
  </xsl:template>

  <xsl:template match="*[self::wix:Component or self::wix:ComponentRef]
                        [key('PdbToRemove', @Id)]" />

  <xsl:template match="*[self::wix:Component or self::wix:ComponentRef]
                        [key('CsToRemove', @Id)]" />

</xsl:stylesheet>