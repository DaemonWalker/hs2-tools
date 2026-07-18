import { FC, useEffect, useState, memo } from 'react'
import { Form, Input, Button, message, Card, Row, Col, Switch } from 'antd'
import { useSettingStore } from '@renderer/store/settingStore'
import { SaveOutlined, ReloadOutlined, GlobalOutlined, MoonOutlined } from '@ant-design/icons'

// 验证代理地址格式
const validateProxyUrl = (url: string): boolean => {
  if (!url) return true // 空值视为有效（不启用代理）
  try {
    const parsed = new URL(url)
    return parsed.protocol === 'http:' || parsed.protocol === 'https:'
  } catch {
    return false
  }
}

export const SystemSettings: FC = memo(() => {
  const [form] = Form.useForm()
  const { settings, setSettingForm } = useSettingStore()
  const [saving, setSaving] = useState(false)

  // 当 settings 加载完成后，更新表单值
  useEffect(() => {
    form.setFieldsValue({
      proxyAddress: settings.proxy?.uri || '',
      proxyUsername: settings.proxy?.username || '',
      proxyPassword: settings.proxy?.password || '',
      disableWindowsSleep: settings.windowsSleep?.disabled ?? false
    })
  }, [form, settings])

  const handleSave = async () => {
    try {
      const values = await form.validateFields()
      setSaving(true)

      await setSettingForm({
        proxy: {
          uri: values.proxyAddress,
          username: values.proxyUsername,
          password: values.proxyPassword
        },
        windowsSleep: { disabled: !!values.disableWindowsSleep }
      })

      message.success('设置已保存')
    } catch (error) {
      if (error instanceof Error) {
        message.error(`保存失败: ${error.message}`)
      }
    } finally {
      setSaving(false)
    }
  }

  const handleReset = () => {
    form.setFieldsValue({
      proxyAddress: settings.proxy?.uri || '',
      proxyUsername: settings.proxy?.username || '',
      proxyPassword: settings.proxy?.password || '',
      disableWindowsSleep: settings.windowsSleep?.disabled ?? false
    })
    message.info('已恢复当前保存的设置')
  }

  return (
    <div style={{ padding: '24px' }}>
      <Form
        form={form}
        layout="vertical"
        autoComplete="off"
        requiredMark={false}
      >
        <Row gutter={[24, 24]}>
          {/* 代理设置 */}
          <Col xs={24} lg={12}>
            <Card
              title={
                <span>
                  <GlobalOutlined style={{ marginRight: 8 }} />
                  代理设置
                </span>
              }
            >
              <Form.Item
                label="代理服务器地址"
                name="proxyAddress"
                rules={[
                  {
                    validator: (_, value) => {
                      if (!value || validateProxyUrl(value)) {
                        return Promise.resolve()
                      }
                      return Promise.reject(new Error('请输入有效的 HTTP/HTTPS 代理地址'))
                    }
                  }
                ]}
                extra="格式如 http://127.0.0.1:7890，留空则不使用代理"
              >
                <Input placeholder="http://127.0.0.1:7890" />
              </Form.Item>

              <Form.Item noStyle shouldUpdate={(prev, curr) => prev.proxyAddress !== curr.proxyAddress}>
                {({ getFieldValue }) => {
                  const proxyAddress = getFieldValue('proxyAddress')
                  return proxyAddress ? (
                    <>
                      <Form.Item
                        label="用户名"
                        name="proxyUsername"
                        extra="如果代理需要认证，请输入用户名"
                      >
                        <Input placeholder="用户名（可选）" />
                      </Form.Item>

                      <Form.Item
                        label="密码"
                        name="proxyPassword"
                        extra="如果代理需要认证，请输入密码"
                      >
                        <Input.Password placeholder="密码（可选）" />
                      </Form.Item>
                    </>
                  ) : null
                }}
              </Form.Item>
            </Card>
          </Col>

          {/* 电源管理 */}
          <Col xs={24} lg={12}>
            <Card
              title={
                <span>
                  <MoonOutlined style={{ marginRight: 8 }} />
                  电源管理
                </span>
              }
            >
              <Form.Item
                label="阻止 Windows 休眠"
                name="disableWindowsSleep"
                valuePropName="checked"
              >
                <Switch
                  checkedChildren="开启"
                  unCheckedChildren="关闭"
                />
              </Form.Item>
              <p style={{ color: 'var(--text-secondary)', fontSize: 12, marginTop: 8 }}>
                开启后，在应用运行时将阻止系统进入休眠状态
              </p>
            </Card>
          </Col>
        </Row>

        {/* 操作按钮 */}
        <Row style={{ marginTop: 24 }}>
          <Col xs={24}>
            <Card>
              <Button
                type="primary"
                icon={<SaveOutlined />}
                onClick={handleSave}
                loading={saving}
                size="large"
                style={{ marginRight: 12 }}
              >
                保存设置
              </Button>
              <Button
                icon={<ReloadOutlined />}
                onClick={handleReset}
                size="large"
              >
                重置
              </Button>
            </Card>
          </Col>
        </Row>
      </Form>
    </div>
  )
})

SystemSettings.displayName = 'SystemSettings'

export default SystemSettings
