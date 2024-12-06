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

  <xsl:template match="*[self::wix:Component or self::wix:ComponentRef]
                        [key('PdbToRemove', @Id)]" />

  <xsl:key name="NonExeConfigToRemove"
           match="wix:Component[contains(wix:File/@Source, '.dll.config')]"
           use="@Id" />

  <xsl:template match="*[self::wix:Component or self::wix:ComponentRef]
                        [key('NonExeConfigToRemove', @Id)]" />

  <xsl:template match="wix:File[@Source='$(var.ToolsReqDir)\Tools.RequestCLI.exe']/@Id">
    <xsl:attribute name="{name()}">
      <xsl:value-of select="'ToolsRequestExe'" />
    </xsl:attribute>
  </xsl:template>

  <xsl:template match="wix:File[@Source='$(var.ToolsReqDir)\appsettings.json']/@Id">
    <xsl:attribute name="{name()}">
      <xsl:value-of select="'ToolsRequestConfig'" />
    </xsl:attribute>
  </xsl:template>

  <xsl:template match="wix:File[@Source='$(var.ToolsDCEBDir)\appsettings.json']/@Id">
    <xsl:attribute name="{name()}">
      <xsl:value-of select="'ToolsDeployCopyEnvBuildConfig'" />
    </xsl:attribute>
  </xsl:template>

  <xsl:template match="wix:File[@Source='$(var.ToolsPREDir)\appsettings.json']/@Id">
    <xsl:attribute name="{name()}">
      <xsl:value-of select="'PostRestoreEndurConfig'" />
    </xsl:attribute>
  </xsl:template>

</xsl:stylesheet>